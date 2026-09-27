using System.Text.RegularExpressions;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Services;

public sealed class FantasySentimentAnalyzer(IOptionsMonitor<SentimentLexiconOptions> options)
{
    private const double TermWeight = 0.36;
    private const double PhraseWeight = 0.56;
    private const double SlangWeight = 0.82;
    private const double SarcasmWeight = -0.95;
    private const double FantasyAdjustmentCap = 0.95;
    private const double EmojiAdjustmentCap = 0.60;
    private const double StructuralAdjustmentCap = 0.85;
    private const double PerspectiveAdjustmentCap = 0.95;

    public SentimentResult Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SentimentResult(0, SentimentCategories.Neutral);
        }

        var preparation = PrepareForVader(text);
        var analysis = AnalyzeTarget(
            text,
            TargetPerspectives.Unknown,
            rawVaderScore: 0,
            preparation);

        return new SentimentResult(
            analysis.FinalScore,
            TargetedSentimentResult.CategoryFromScore(analysis.FinalScore));
    }

    public FantasySemanticPreparation PrepareForVader(string targetText)
    {
        var lexicon = options.CurrentValue;
        var emojiWeights = EmojiWeights(lexicon).ToList();
        if (emojiWeights.Count == 0 || string.IsNullOrWhiteSpace(targetText))
        {
            return new FantasySemanticPreparation(
                targetText,
                0,
                [],
                []);
        }

        var values = new List<double>();
        var matchedEmoji = new List<string>();
        var appliedRules = new List<string>();
        var vaderText = Regex.Replace(
            targetText,
            @"\bgarbage\s+time\b",
            "late game time",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (var (emoji, weight) in emojiWeights)
        {
            var count = CountOccurrences(vaderText, emoji);
            if (count == 0)
            {
                continue;
            }

            for (var index = 0; index < count; index++)
            {
                values.Add(weight);
            }

            matchedEmoji.Add($"{emoji} {FormatSigned(weight)}");
            appliedRules.Add($"emoji:{emoji}={FormatSigned(weight)}");
            vaderText = vaderText.Replace(emoji, " ", StringComparison.Ordinal);
        }

        return new FantasySemanticPreparation(
            CollapseWhitespace(vaderText),
            DiminishingSum(values, EmojiAdjustmentCap),
            matchedEmoji,
            appliedRules);
    }

    public FantasySemanticAnalysis AnalyzeTarget(
        string targetText,
        string perspective,
        double rawVaderScore,
        FantasySemanticPreparation preparation,
        PlayerMentionMatch? target = null)
    {
        if (string.IsNullOrWhiteSpace(targetText))
        {
            return new FantasySemanticAnalysis(
                0,
                preparation.EmojiAdjustment,
                0,
                0,
                0,
                0.15,
                Math.Round(Math.Clamp(rawVaderScore + preparation.EmojiAdjustment, -1, 1), 3),
                [],
                [],
                [],
                preparation.MatchedEmoji,
                preparation.AppliedRules);
        }

        var lexicon = options.CurrentValue;
        var normalizedText = NormalizePhrase(preparation.VaderText);
        var targetAliases = TargetAliases(target);
        var consumedSpans = new List<TextSpan>();
        var matches = new List<SemanticTermMatch>();
        var appliedRules = new List<string>(preparation.AppliedRules);

        if (IsNameOnlyMockeryReaction(normalizedText, targetAliases) && preparation.MatchedEmoji.Count == 0)
        {
            const double score = -0.32;
            appliedRules.Add($"nameOnlyMockeryNegative={FormatSigned(score)}");
            return new FantasySemanticAnalysis(
                0,
                0,
                score,
                0,
                0,
                0.55,
                score,
                [],
                [],
                [],
                [],
                appliedRules);
        }

        if (IsNameOnlyReaction(normalizedText, targetAliases) && preparation.MatchedEmoji.Count == 0)
        {
            appliedRules.Add("nameOnlyReactionNeutral");
            return new FantasySemanticAnalysis(
                0,
                0,
                0,
                0,
                0,
                0.15,
                0,
                [],
                [],
                [],
                [],
                appliedRules);
        }

        foreach (var sarcasmMatch in MatchLowPointSarcasm(normalizedText, lexicon))
        {
            matches.Add(sarcasmMatch);
            consumedSpans.Add(sarcasmMatch.Span);
            appliedRules.Add($"sarcasm:{sarcasmMatch.Text}");
        }

        foreach (var entry in BuildEntries(lexicon))
        {
            foreach (Match match in Regex.Matches(
                         normalizedText,
                         BuildPhrasePattern(entry.NormalizedText),
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                var span = new TextSpan(match.Index, match.Length);
                if (consumedSpans.Any(existing => existing.Overlaps(span)))
                {
                    continue;
                }

                if (ShouldSkipEntryMatch(entry.NormalizedText, normalizedText, span))
                {
                    appliedRules.Add($"contextSkipped:{entry.Text}");
                    continue;
                }

                var wordsBefore = WordsBefore(normalizedText, match.Index, 5);
                var hasNegator = wordsBefore.Any(word => lexicon.Negators.Contains(word, StringComparer.OrdinalIgnoreCase));
                var hasIntensifier = wordsBefore.Any(word => lexicon.Intensifiers.Contains(word, StringComparer.OrdinalIgnoreCase));
                var score = entry.Score;
                if (hasNegator)
                {
                    score *= -1;
                }

                if (hasIntensifier)
                {
                    score *= 1.25;
                }

                matches.Add(new SemanticTermMatch(
                    entry.Text,
                    Math.Round(score, 3),
                    entry.IsPhrase,
                    span));
                consumedSpans.Add(span);
            }
        }

        var fantasyAdjustment = DiminishingSum(matches.Select(match => match.Score), FantasyAdjustmentCap);
        var structuralAdjustment = CalculateStructuralAdjustment(
            normalizedText,
            targetAliases,
            appliedRules);
        var perspectiveAdjustment = CalculatePerspectiveAdjustment(
            normalizedText,
            perspective,
            rawVaderScore,
            fantasyAdjustment,
            structuralAdjustment,
            matches,
            targetAliases,
            lexicon,
            appliedRules);
        var baseBeforeQuestion = Math.Clamp(
            rawVaderScore + fantasyAdjustment + preparation.EmojiAdjustment + structuralAdjustment + perspectiveAdjustment,
            -1,
            1);
        var questionAdjustment = CalculateQuestionAdjustment(
            normalizedText,
            targetText,
            baseBeforeQuestion,
            appliedRules);
        var finalScore = Math.Round(Math.Clamp(baseBeforeQuestion + questionAdjustment, -1, 1), 3);
        var semanticConfidence = CalculateSemanticConfidence(
            finalScore,
            fantasyAdjustment,
            preparation.EmojiAdjustment,
            structuralAdjustment,
            perspectiveAdjustment,
            questionAdjustment,
            matches);

        var positiveTerms = matches
            .Where(match => match.Score > 0)
            .Select(match => match.Text)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var negativeTerms = matches
            .Where(match => match.Score < 0)
            .Select(match => match.Text)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var phrases = matches
            .Where(match => match.IsPhrase)
            .Select(match => match.Text)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var match in matches)
        {
            appliedRules.Add($"{(match.Score >= 0 ? "positive" : "negative")}:{match.Text}={FormatSigned(match.Score)}");
        }

        if (Math.Abs(fantasyAdjustment) >= 0.001)
        {
            appliedRules.Add($"fantasyAdjustment={FormatSigned(fantasyAdjustment)}");
        }

        return new FantasySemanticAnalysis(
            fantasyAdjustment,
            preparation.EmojiAdjustment,
            structuralAdjustment,
            perspectiveAdjustment,
            questionAdjustment,
            semanticConfidence,
            finalScore,
            positiveTerms,
            negativeTerms,
            phrases,
            preparation.MatchedEmoji,
            appliedRules.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static double CalculatePerspectiveAdjustment(
        string normalizedText,
        string perspective,
        double rawVaderScore,
        double fantasyAdjustment,
        double structuralAdjustment,
        IReadOnlyList<SemanticTermMatch> matches,
        IReadOnlyList<string> targetAliases,
        SentimentLexiconOptions lexicon,
        List<string> appliedRules)
    {
        var adjustment = 0.0;
        var hasTargetAliases = targetAliases.Count > 0;
        var hasTargetNegativeEvaluation = !hasTargetAliases
                                          ? fantasyAdjustment <= -0.20 || matches.Any(match => match.Score < -0.40)
                                          : HasTargetNegativeEvaluation(normalizedText, targetAliases);
        var hasExplicitNegative = structuralAdjustment < -0.20
                                  || hasTargetNegativeEvaluation;
        var hasExplicitPositive = fantasyAdjustment >= 0.20
                                  || matches.Any(match => match.Score > 0.40);

        if (string.Equals(perspective, TargetPerspectives.OpponentRoster, StringComparison.OrdinalIgnoreCase))
        {
            var hasDirectTargetThreat = HasOpponentThreatAgainstTarget(normalizedText, targetAliases);
            var hasThreatLanguage = ContainsAny(normalizedText, lexicon.PerspectivePatterns.OpponentThreat)
                                    || hasDirectTargetThreat
                                    || (Regex.IsMatch(
                                            normalizedText,
                                            @"\b(?:playing against|played against|facing|against me)\b",
                                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                                        && Regex.IsMatch(
                                            normalizedText,
                                            @"\b(?:wish|regret|hate|over|done|rip|lost|toast|cooked)\b",
                                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

            if (hasThreatLanguage && !hasExplicitNegative)
            {
                adjustment = hasDirectTargetThreat && rawVaderScore < -0.10
                    ? 0.90
                    : rawVaderScore < -0.10
                        ? 0.75
                        : 0.55;
                appliedRules.Add($"opponentThreat={FormatSigned(adjustment)}");
            }
        }
        else if (string.Equals(perspective, TargetPerspectives.OwnRoster, StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsAny(normalizedText, lexicon.PerspectivePatterns.OwnRosterRegret))
            {
                adjustment = -0.62;
                appliedRules.Add($"ownRosterRegret={FormatSigned(adjustment)}");
            }
            else if (ContainsAny(normalizedText, lexicon.PerspectivePatterns.OwnRosterPositive) && !hasExplicitNegative)
            {
                adjustment = hasExplicitPositive ? 0.15 : 0.30;
                appliedRules.Add($"ownRosterPositive={FormatSigned(adjustment)}");
            }
        }

        return Math.Round(Math.Clamp(adjustment, -PerspectiveAdjustmentCap, PerspectiveAdjustmentCap), 3);
    }

    private static double CalculateStructuralAdjustment(
        string normalizedText,
        IReadOnlyList<string> targetAliases,
        List<string> appliedRules)
    {
        if (targetAliases.Count == 0 || normalizedText.Length == 0)
        {
            return 0;
        }

        var scores = new List<double>();
        AddStructuralScore(
            Regex.IsMatch(
                normalizedText,
                @"\b(?:(?:qb|rb|wr|te)[2-9]\s+at\s+best|only\s+a\s+(?:qb|rb|wr|te)[2-9]|nothing\s+more\s+than\s+a\s+(?:qb|rb|wr|te)[2-9])\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            -0.85,
            "fantasyTierAtBest");

        AddStructuralScore(
            Regex.IsMatch(
                normalizedText,
                @"\bfreak\s+of\s+nature\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            0.30,
            "freakOfNaturePositive");

        foreach (var alias in targetAliases)
        {
            var aliasPattern = AliasPattern(alias);
            if (aliasPattern.Length == 0)
            {
                continue;
            }

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\bwhy\s+(?:tf\s+)?did\s+i\s+(?:start|play|played|started)\s+{aliasPattern}\b|\b(?:cant|can't|cannot)\s+believe\s+i\s+(?:started|played)\s+{aliasPattern}\b|\bi\s+(?:started|played)\s+{aliasPattern}\b.{{0,60}}\bclown\b|\bguess\s+who\s+(?:started|played)\b.{{0,80}}{aliasPattern}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.65,
                "lineupRegret");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\bwhy\s+(?:tf\s+)?did\s+i\s+draft\s+{aliasPattern}\b|\b(?:cant|can't|cannot)\s+believe\s+i\s+drafted\s+{aliasPattern}\b|\bdrafted\s+{aliasPattern}\b.{{0,80}}\b(?:too\s+high|mistake|regret)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.55,
                "draftRegret");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\b(?:started|starting)\s+{aliasPattern}\b.{{0,40}}\bama\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.65,
                "lineupRegretAma");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\bdrafted\s+{aliasPattern}\b.{{0,40}}\bama\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.58,
                "draftRegretAma");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\b(?:benched|benching)\s+{aliasPattern}\b.{{0,40}}\bama\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.62,
                "benchRegretAmaPositive");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\b(?:(?:i|we)\s+(?:am\s+|are\s+)?|i'm\s+|we're\s+)?(?:already\s+)?(?:drop|dropping|dropped)\s+{aliasPattern}\b|\b(?:gonna|going\s+to|about\s+to|can't\s+wait\s+to|cant\s+wait\s+to)\s+drop\s+{aliasPattern}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                && !Regex.IsMatch(
                    normalizedText,
                    $@"\b(?:should|can|do)\s+(?:i|we)\s+drop\s+{aliasPattern}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.70,
                "dropTargetNegative");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\b(?:drop|dropping|dropped)\b.{{0,100}}\bfor\s+{aliasPattern}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.45,
                "dropReplacementPositive");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"{aliasPattern}\s+(?:to|straight\s+to)\s+(?:the\s+)?(?:waiver\s+wire|waivers)\b|\bsend\s+{aliasPattern}\s+to\s+(?:the\s+)?(?:waiver\s+wire|waivers)\b|{aliasPattern}\b.{{0,80}}\bwelcome\s+to\s+(?:the\s+)?(?:waiver\s+wire|waivers)\b|{aliasPattern}\b.{{0,40}}\b(?:is|looks?|seems?)\s+(?:a\s+)?(?:waiver\s+player|waiver\s+wire\s+player)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.65,
                "waiverDropNegative");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\b(?:pick\s+up|pickup|grab|claim|scoop)\s+{aliasPattern}\b.{{0,40}}\b(?:off|on|from)\s+waivers?\b|\b(?:best|top)\s+waiver\s+(?:add|pickup)\b.{{0,80}}{aliasPattern}\b|{aliasPattern}\b.{{0,80}}\b(?:best|top)\s+waiver\s+(?:add|pickup)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.45,
                "waiverAddPositive");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\bshould\s+have\s+started\s+{aliasPattern}\s+over\b|\bcould\s+have\s+had\s+{aliasPattern}\b.{{0,100}}\b(?:but\s+)?(?:i\s+)?(?:chose|took|drafted|started)\b|\b(?:should've|shouldve)\s+started\s+{aliasPattern}\s+over\b|\b(?:needed|shoulda)\s+started\s+{aliasPattern}\s+over\b|\b(?:started|played)\s+the\s+wrong\s+\w+\b.{{0,80}}{aliasPattern}",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.60,
                "comparisonBenefit");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\bshould\s+have\s+started\b.{{0,100}}\bover\s+{aliasPattern}\b|\bcould\s+have\s+had\b.{{0,100}}\b(?:but\s+)?(?:i\s+)?(?:chose|took|drafted|started)\s+{aliasPattern}\b|\b(?:should've|shouldve)\s+started\b.{{0,100}}\bover\s+{aliasPattern}\b|\b(?:needed|shoulda)\s+started\b.{{0,100}}\bover\s+{aliasPattern}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.60,
                "comparisonRegret");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\btaking\s+{aliasPattern}\s+over\b.{{0,100}}\b(?:haunt|regret|mistake|brutal|sick)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.60,
                "comparisonRegret");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\btaking\b.{{0,100}}\bover\s+{aliasPattern}\b.{{0,100}}\b(?:haunt|regret|mistake|brutal|sick)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.60,
                "comparisonBenefit");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"{aliasPattern}\b.{{0,40}}\b(?:cooked|cooking)\s+(?:the|them|that|this|those|these|tf|defense|secondary)|{aliasPattern}\b.{{0,60}}\bgoing\s+off\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.48,
                "targetPerformancePositive");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"{aliasPattern}\b.{{0,40}}\bsave\s+(?:me|us)\s+from\b|\b(?:need|needing)\s+{aliasPattern}\b.{{0,40}}\bto\s+save\s+(?:me|us)\s+from\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.55,
                "saveMePositive");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\bsave\s+(?:me|us)\s+from\b.{{0,80}}{aliasPattern}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.65,
                "saveMeFromNegative");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\b(?:benching|bench|sitting|sit)\b.{{0,80}}{aliasPattern}\b.{{0,80}}\bfor\b|\b(?:benching|bench|sitting|sit)\b.{{0,60}}{aliasPattern}\b.{{0,40}}\bnot\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.45,
                "sitAction");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\b(?:benching|bench|sitting|sit)\b.{{0,100}}\bfor\b.{{0,80}}{aliasPattern}\b|\b(?:benching|bench|sitting|sit)\b.{{0,80}}\bnot\b.{{0,40}}{aliasPattern}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.45,
                "startOverBenchedPlayer");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"{aliasPattern}\b.{{0,50}}\b(?:who|but|actually)\s+sucks\b|{aliasPattern}\b.{{0,80}}\b(?:is|was)\s+not\s+(?:in\s+fact\s+)?(?:a\s+|the\s+)?(?:beast|beastmode|him|goat|dawg|truth|elite|good|great|legit)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.90,
                "reversalNegative");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"{aliasPattern}\b.{{0,40}}\b(?:td|touchdown)\b|{aliasPattern}\b.{{0,50}}\bgets?\s+(?:the\s+)?first\s+(?:td|touchdown)\b|\b(?:td|touchdown)\s+{aliasPattern}\b|\b(?:td|touchdown)\b.{{0,30}}{aliasPattern}\b|{aliasPattern}\b.{{0,70}}\b(?:making|makes|made|caught|catches?|gets?)\s+(?:a\s+)?catch\b|\b(?:they|we)\s+feed\s+{aliasPattern}\b.{{0,40}}\bend\s+zone\b|{aliasPattern}\b.{{0,80}}\b(?:target|targets)\b.{{0,30}}\b(?:feasting|truthers\s+feasting)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.62,
                "productionEventPositive");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\b(?:need|just\s+need)\s+{aliasPattern}\b.{{0,80}}\b(?:destroy|breakaway|td|touchdown|go\s+off|finish\s+it)\b|{aliasPattern}\b.{{0,80}}\b(?:td\s+finish\s+it|breakaway\s+run)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.75,
                "desiredProductionPositive");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"{aliasPattern}\b.{{0,80}}\b(?:gonna|going\s+to)\s+feed\s+families\b|\bwe\s+actually\s+did\s+it\s+with\s+{aliasPattern}\b|{aliasPattern}\b.{{0,80}}\b(?:truthers\s+)?feasting\b|\b(?:garbage|late\s+game)\s+time\b.{{0,40}}{aliasPattern}\b.{{0,40}}\bincoming\b|{aliasPattern}\b.{{0,60}}\b(?:my\s+boi|boi|boy)\b|\b(?:that'?s|thats)\s+my\s+(?:boi|boy)\s+{aliasPattern}\b|{aliasPattern}\b.{{0,80}}\bselected\s+answer\b|\bselected\s+answer\b.{{0,80}}{aliasPattern}\b|\bmy\s+{aliasPattern}\b.{{0,80}}\bvs\b.{{0,80}}\bcmon\b|\bher\s+{aliasPattern}\b.{{0,80}}\bcmon\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.75,
                "fantasyProductionPositive");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\b(?:need|want|just\s+want)\s+{aliasPattern}\b.{{0,80}}\b(?:to\s+do\s+more|to\s+do\s+something|to\s+finally\s+get\s+his|to\s+finally\s+get\s+theirs)\b|\bfeed\s+{aliasPattern}\s+(?:pls|please)\b|\b(?:wish\s+i\s+could|can'?t\s+wait\s+to|cant\s+wait\s+to)\s+drop\s+{aliasPattern}\b|\b(?:wanted|want|needed)\s+(?:a\s+)?{aliasPattern}\s+(?:td|touchdown)\b|\bplease\s+{aliasPattern}\s+(?:td|touchdown)\b|{aliasPattern}\b.{{0,80}}\byikes\b|\bfucking\s+{aliasPattern}\b$|\bkilling\s+my\s+{aliasPattern}\s+stonks\b|{aliasPattern}\b.{{0,40}}\bkilling\s+my\b.{{0,60}}\bstonks\b|\bnot\s+throwing\s+to\s+{aliasPattern}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.85,
                "usageConcernNegative");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"{aliasPattern}\b.{{0,100}}\b(?:throw|throws|throwing)\s+the\s+ball\b.{{0,80}}\b(?:close\s+my\s+eyes|same\s+as)|\bclose\s+my\s+eyes\b.{{0,100}}{aliasPattern}\b.{{0,80}}\b(?:throw|throws|throwing)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.62,
                "quarterbackConcernNegative");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"{aliasPattern}\b.{{0,50}}\b(?:is\s+)?better\s+than\b|{aliasPattern}\b.{{0,40}}\bclears\b|{aliasPattern}\b.{{0,70}}\b(?:more|so|super|very)\s+explosive\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.60,
                "comparisonPositive");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\bbetter\s+than\s+{aliasPattern}\b|\bmore\s+\w+\s+than\s+{aliasPattern}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.60,
                "comparisonNegative");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"{aliasPattern}\b.{{0,100}}\b(?:fun\s+to\s+watch|carry\s+this\s+team|carries\s+this\s+team|whole\s+team|madden\s+mode|that\s+dude|beautiful\s+bastard|best\s+(?:rb|wr|te|qb)\s+in|automatic\s+flex\s+play)\b|\b(?:die\s+for|would\s+die\s+for|thank\s+you\s+daddy)\s+{aliasPattern}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.85,
                "performancePraisePositive");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"{aliasPattern}\b.{{0,70}}\b(?:(?:is|was|looks?|looked|seems?|seemed|plays?|playing)\s+(?:so\s+|very\s+|really\s+|straight\s+|like\s+)?(?:awful|terrible|bad|slow|dead|washed|cooked|trash|garbage|non\s+existent|nonexistent|invisible|horrible|ass|buns|dogshit|mid|fraud|bum|stinks)|(?:so\s+|very\s+|really\s+|straight\s+)?(?:slow|trash|garbage|non\s+existent|nonexistent|invisible|horrible|ass|buns|dogshit|mid|fraud|bum|stinks))\b|\b(?:is|was|looks?|looked|seems?|seemed)\s+{aliasPattern}\s+(?:awful|terrible|bad|slow|dead|washed|cooked|trash|garbage|non\s+existent|nonexistent|invisible|horrible|ass|buns|dogshit|mid|fraud|bum|stinks)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.58,
                "targetEvaluationNegative");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"{aliasPattern}\b.{{0,80}}\b(?:comes\s+out|came\s+out)\b.{{0,80}}\bdrive\s+dies\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                -0.58,
                "targetRemovedDriveNegative");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"{aliasPattern}\b.{{0,80}}\bfell\s+to\s+me\b(?:.{{0,80}}\b(?:value|steal)\b)?",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.58,
                "draftValuePositive");

            AddStructuralScore(
                Regex.IsMatch(
                    normalizedText,
                    $@"\bmultiple\s+catches\b.{{0,80}}\bpromising\b|{aliasPattern}\b.{{0,80}}\bcould'?ve\s+ha(?:d|ve)\b.{{0,60}}\b(?:td|touchdown)s?\b|{aliasPattern}\b.{{0,80}}\b(?:out\s+there\s+)?gliding\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                0.95,
                "opportunityPositive");
        }

        return DiminishingSum(scores, StructuralAdjustmentCap);

        void AddStructuralScore(bool condition, double score, string rule)
        {
            if (!condition)
            {
                return;
            }

            scores.Add(score);
            appliedRules.Add($"{rule}={FormatSigned(score)}");
        }
    }

    private static double CalculateQuestionAdjustment(
        string normalizedText,
        string originalText,
        double baseBeforeQuestion,
        List<string> appliedRules)
    {
        if (!IsQuestionLike(normalizedText, originalText))
        {
            return 0;
        }

        var concernQuestion = Regex.IsMatch(
            normalizedText,
            @"\b(?:should i|should we|can i|can we|is|are|how sustainable|why did i)\b.{0,80}\b(?:drop|droppable|trust|hurt|startable|bench|sell|panic|worried)\b|\bwhy did i start\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        double adjustment;
        if (concernQuestion && baseBeforeQuestion < 0 && Math.Abs(baseBeforeQuestion) < 0.35)
        {
            adjustment = -0.12 - baseBeforeQuestion;
        }
        else if (concernQuestion && Math.Abs(baseBeforeQuestion) < 0.15)
        {
            adjustment = -0.12;
        }
        else if (Math.Abs(baseBeforeQuestion) >= 0.20)
        {
            adjustment = -baseBeforeQuestion * 0.18;
        }
        else
        {
            adjustment = 0;
        }

        adjustment = Math.Round(adjustment, 3);
        if (Math.Abs(adjustment) >= 0.001)
        {
            appliedRules.Add($"questionDamping={FormatSigned(adjustment)}");
        }

        return adjustment;
    }

    private static bool IsQuestionLike(string normalizedText, string originalText)
    {
        if (originalText.Contains('?'))
        {
            return true;
        }

        return Regex.IsMatch(
            normalizedText,
            @"^(?:should i|should we|can i|can we|is |are |how sustainable|how is|why did i|who should|what do we)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static IReadOnlyList<SemanticTermMatch> MatchLowPointSarcasm(
        string normalizedText,
        SentimentLexiconOptions lexicon)
    {
        var matches = new List<SemanticTermMatch>();
        var patterns = new[]
        {
            @"\bthanks\s+for\s+(?:the\s+)?(?<points>\d+(?:\.\d+)?)\s+points?\b",
            @"\blove\s+(?:the\s+)?(?<points>\d+(?:\.\d+)?)\s+points?\b",
            @"\bgreat\s+another\s+(?<points>\d+(?:\.\d+)?)\s+points?\s+(?:game|night)?\b"
        };

        foreach (var pattern in patterns)
        {
            foreach (Match match in Regex.Matches(
                         normalizedText,
                         pattern,
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                if (!double.TryParse(match.Groups["points"].Value, out var points)
                    || points > lexicon.Sarcasm.LowPointMaximum)
                {
                    continue;
                }

                matches.Add(new SemanticTermMatch(
                    match.Value,
                    SarcasmWeight,
                    true,
                    new TextSpan(match.Index, match.Length)));
            }
        }

        return matches;
    }

    private static IEnumerable<LexiconEntry> BuildEntries(SentimentLexiconOptions lexicon)
    {
        var entries = new Dictionary<string, LexiconEntry>(StringComparer.OrdinalIgnoreCase);

        AddEntries(lexicon.PositiveSlang, SlangWeight, isPhrase: false);
        AddEntries(lexicon.NegativeSlang, -SlangWeight, isPhrase: false);
        AddEntries(lexicon.PositivePhrases, PhraseWeight, isPhrase: true);
        AddEntries(lexicon.NegativePhrases, -PhraseWeight, isPhrase: true);
        AddLegacyEntries(lexicon.PositiveTerms, 1);
        AddLegacyEntries(lexicon.NegativeTerms, -1);

        return entries.Values
            .OrderByDescending(entry => entry.WordCount)
            .ThenByDescending(entry => Math.Abs(entry.Score))
            .ThenByDescending(entry => entry.NormalizedText.Length)
            .ToList();

        void AddLegacyEntries(IEnumerable<string> terms, int polarity)
        {
            foreach (var term in terms)
            {
                var normalized = NormalizePhrase(term);
                if (normalized.Length == 0)
                {
                    continue;
                }

                var isPhrase = normalized.Contains(' ');
                var weight = isPhrase ? PhraseWeight : TermWeight;
                AddEntry(term, polarity * weight, isPhrase);
            }
        }

        void AddEntries(IEnumerable<string> values, double score, bool isPhrase)
        {
            foreach (var value in values)
            {
                AddEntry(value, score, isPhrase || NormalizePhrase(value).Contains(' '));
            }
        }

        void AddEntry(string text, double score, bool isPhrase)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var normalized = NormalizePhrase(text);
            if (normalized.Length == 0)
            {
                return;
            }

            var candidate = new LexiconEntry(
                text,
                normalized,
                score,
                isPhrase,
                normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
            if (!entries.TryGetValue(normalized, out var existing)
                || Math.Abs(candidate.Score) > Math.Abs(existing.Score))
            {
                entries[normalized] = candidate;
            }
        }
    }

    private static IEnumerable<(string Emoji, double Weight)> EmojiWeights(SentimentLexiconOptions lexicon)
    {
        foreach (var pair in lexicon.EmojiLexicon.Positive)
        {
            yield return (pair.Key, pair.Value);
        }

        foreach (var pair in lexicon.EmojiLexicon.Negative)
        {
            yield return (pair.Key, pair.Value);
        }
    }

    private static bool ContainsAny(string normalizedText, IEnumerable<string> patterns)
    {
        return patterns
            .Select(NormalizePhrase)
            .Where(pattern => pattern.Length > 0)
            .Any(pattern => Regex.IsMatch(
                normalizedText,
                BuildPhrasePattern(pattern),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    private static bool ShouldSkipEntryMatch(
        string normalizedEntry,
        string normalizedText,
        TextSpan span)
    {
        if ((normalizedEntry.Equals("garbage", StringComparison.OrdinalIgnoreCase)
             || normalizedEntry.Equals("trash", StringComparison.OrdinalIgnoreCase))
            && Regex.IsMatch(
                normalizedText,
                @"\b(?:garbage|trash)\s+time\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return true;
        }

        if (normalizedEntry.Equals("love", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(
                normalizedText[span.Start..Math.Min(normalizedText.Length, span.End + 16)],
                @"^love\s+(?:is|was|looks?|looked|throws?|throwing|killing|has|had|needs?|seems?)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return true;
        }

        if (!normalizedEntry.Equals("cooked", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var cookedContextStart = Math.Max(0, span.Start - 24);
        var cookedContextLength = Math.Min(normalizedText.Length - cookedContextStart, span.Length + 72);
        var cookedContext = normalizedText.Substring(cookedContextStart, cookedContextLength);
        return Regex.IsMatch(
                   cookedContext,
                   @"\b(?:i\s+am|i'm|im|we\s+are|we're|were)\s+cooked\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || Regex.IsMatch(
                   cookedContext,
                   @"\bcooked\s+(?:the|them|that|this|those|these|tf|defense|secondary)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsNameOnlyReaction(
        string normalizedText,
        IReadOnlyList<string> targetAliases)
    {
        if (normalizedText.Length == 0 || targetAliases.Count == 0)
        {
            return false;
        }

        foreach (var alias in targetAliases)
        {
            var aliasPattern = AliasPattern(alias);
            if (aliasPattern.Length == 0)
            {
                continue;
            }

            if (Regex.IsMatch(
                    normalizedText,
                    $@"^(?:(?:holy\s+(?:shit|fuck)|wow|damn|bro|lol|lmao+)\s+)?{aliasPattern}(?:\s+(?:holy\s+(?:shit|fuck)|wow|damn|bro|lol|lmao+))?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNameOnlyMockeryReaction(
        string normalizedText,
        IReadOnlyList<string> targetAliases)
    {
        if (normalizedText.Length == 0 || targetAliases.Count == 0)
        {
            return false;
        }

        foreach (var alias in targetAliases)
        {
            var aliasPattern = AliasPattern(alias);
            if (aliasPattern.Length == 0)
            {
                continue;
            }

            if (Regex.IsMatch(
                    normalizedText,
                    $@"^(?:lmao+\s+{aliasPattern}|{aliasPattern}\s+lmao+)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTargetNegativeEvaluation(
        string normalizedText,
        IReadOnlyList<string> targetAliases)
    {
        var negativeDescriptor =
            @"(?:awful|terrible|bad|washed|cooked|useless|unstartable|trash|garbage|mid|fraud|bum|irrelevant|droppable|benchable|ass|buns|dogshit|invisible)";
        foreach (var alias in targetAliases)
        {
            var aliasPattern = AliasPattern(alias);
            if (aliasPattern.Length == 0)
            {
                continue;
            }

            if (Regex.IsMatch(
                    normalizedText,
                    $@"{aliasPattern}\s+(?:is|was|looks|looked|has\s+been|plays|playing|went)\s+(?:like\s+)?{negativeDescriptor}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || Regex.IsMatch(
                    normalizedText,
                    $@"{aliasPattern}\b.{{0,80}}\b(?:he|him|they)\s+(?:is|was|looks|looked|has\s+been|plays|playing|went)\s+(?:like\s+)?{negativeDescriptor}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasOpponentThreatAgainstTarget(
        string normalizedText,
        IReadOnlyList<string> targetAliases)
    {
        foreach (var alias in targetAliases)
        {
            var aliasPattern = AliasPattern(alias);
            if (aliasPattern.Length == 0)
            {
                continue;
            }

            if (Regex.IsMatch(
                    normalizedText,
                    $@"\b(?:facing|faced)\s+{aliasPattern}\b|\b(?:i\s+am|i'm|im)\s+facing\s+{aliasPattern}\b|\bopp(?:onent)?\s+(?:has\s+)?{aliasPattern}\b|{aliasPattern}\b.{{0,60}}\b(?:killing|destroying|burying|booming|outscoring|fucking)\s+me\b|{aliasPattern}\b.{{0,60}}\b(?:ended|ruined|buried|boomed)\s+my\s+(?:week|matchup)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        return false;
    }

    private static double CalculateSemanticConfidence(
        double finalScore,
        double fantasyAdjustment,
        double emojiAdjustment,
        double structuralAdjustment,
        double perspectiveAdjustment,
        double questionAdjustment,
        IReadOnlyList<SemanticTermMatch> matches)
    {
        if (matches.Count == 0
            && Math.Abs(fantasyAdjustment) < 0.001
            && Math.Abs(emojiAdjustment) < 0.001
            && Math.Abs(structuralAdjustment) < 0.001
            && Math.Abs(perspectiveAdjustment) < 0.001
            && Math.Abs(questionAdjustment) < 0.001
            && Math.Abs(finalScore) < 0.15)
        {
            return 0.20;
        }

        if (Math.Abs(structuralAdjustment) >= 0.40
            || matches.Any(match => match.IsPhrase && Math.Abs(match.Score) >= 0.50)
            || Math.Abs(finalScore) >= 0.55)
        {
            return 0.85;
        }

        if (matches.Count > 0
            || Math.Abs(emojiAdjustment) >= 0.20
            || Math.Abs(perspectiveAdjustment) >= 0.20
            || Math.Abs(questionAdjustment) >= 0.05)
        {
            return 0.60;
        }

        return 0.40;
    }

    private static string BuildPhrasePattern(string normalizedPhrase)
    {
        return $@"(?<![a-z0-9]){Regex.Escape(normalizedPhrase).Replace("\\ ", "\\s+")}(?![a-z0-9])";
    }

    private static string AliasPattern(string normalizedAlias)
    {
        if (string.IsNullOrWhiteSpace(normalizedAlias))
        {
            return string.Empty;
        }

        return BuildPhrasePattern(normalizedAlias);
    }

    private static double DiminishingSum(IEnumerable<double> scores, double cap)
    {
        var ordered = scores
            .Where(score => Math.Abs(score) >= 0.001)
            .OrderByDescending(score => Math.Abs(score))
            .ToList();
        var total = 0.0;
        var multiplier = 1.0;

        foreach (var score in ordered)
        {
            total += score * multiplier;
            multiplier *= 0.45;
        }

        return Math.Round(Math.Clamp(total, -cap, cap), 3);
    }

    private static string NormalizePhrase(string value)
    {
        var lowered = value.ToLowerInvariant();
        lowered = lowered
            .Replace('’', '\'')
            .Replace('‘', '\'');
        lowered = lowered.Replace("🤡", " clown ", StringComparison.Ordinal);
        lowered = Regex.Replace(
            lowered,
            @"\b(?:let's|lets)\s+go+\b",
            "lets go",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        lowered = Regex.Replace(
            lowered,
            @"\blfg+\b",
            "lfg",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var normalized = Regex.Replace(lowered, @"[^a-z0-9']+", " ");
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private static IReadOnlyList<string> TargetAliases(PlayerMentionMatch? target)
    {
        if (target is null)
        {
            return [];
        }

        var aliases = new List<string>
        {
            target.MatchedAlias,
            target.PlayerName
        };

        var lastName = target.PlayerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (!string.IsNullOrWhiteSpace(lastName))
        {
            aliases.Add(lastName);
        }

        return aliases
            .Select(NormalizePhrase)
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string CollapseWhitespace(string value)
    {
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static IReadOnlyList<string> WordsBefore(string text, int index, int count)
    {
        var prefix = text[..Math.Max(0, index)];
        return Regex.Matches(prefix, @"[a-z0-9']+")
            .Select(match => match.Value)
            .TakeLast(count)
            .ToList();
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while (index < text.Length)
        {
            index = text.IndexOf(value, index, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FormatSigned(double value)
    {
        return value >= 0 ? $"+{value:0.###}" : $"{value:0.###}";
    }

    private sealed record LexiconEntry(
        string Text,
        string NormalizedText,
        double Score,
        bool IsPhrase,
        int WordCount);

    private sealed record SemanticTermMatch(
        string Text,
        double Score,
        bool IsPhrase,
        TextSpan Span);

    private sealed record TextSpan(int Start, int Length)
    {
        public int End => Start + Length;

        public bool Overlaps(TextSpan other)
        {
            return Start < other.End && other.Start < End;
        }
    }
}

public sealed record FantasySemanticPreparation(
    string VaderText,
    double EmojiAdjustment,
    IReadOnlyList<string> MatchedEmoji,
    IReadOnlyList<string> AppliedRules);

public sealed record FantasySemanticAnalysis(
    double FantasyAdjustment,
    double EmojiAdjustment,
    double StructuralAdjustment,
    double PerspectiveAdjustment,
    double QuestionAdjustment,
    double SemanticConfidence,
    double FinalScore,
    IReadOnlyList<string> MatchedPositiveTerms,
    IReadOnlyList<string> MatchedNegativeTerms,
    IReadOnlyList<string> MatchedPhrases,
    IReadOnlyList<string> MatchedEmoji,
    IReadOnlyList<string> AppliedRules);

public sealed record SentimentResult(double Score, string Category);
