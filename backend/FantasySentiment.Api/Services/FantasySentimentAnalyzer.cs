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
    private const double PerspectiveAdjustmentCap = 0.75;

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
        var vaderText = targetText;

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
        FantasySemanticPreparation preparation)
    {
        if (string.IsNullOrWhiteSpace(targetText))
        {
            return new FantasySemanticAnalysis(
                0,
                preparation.EmojiAdjustment,
                0,
                0,
                Math.Round(Math.Clamp(rawVaderScore + preparation.EmojiAdjustment, -1, 1), 3),
                [],
                [],
                [],
                preparation.MatchedEmoji,
                preparation.AppliedRules);
        }

        var lexicon = options.CurrentValue;
        var normalizedText = NormalizePhrase(preparation.VaderText);
        var consumedSpans = new List<TextSpan>();
        var matches = new List<SemanticTermMatch>();
        var appliedRules = new List<string>(preparation.AppliedRules);

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

                var wordsBefore = WordsBefore(normalizedText, match.Index, 3);
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
        var perspectiveAdjustment = CalculatePerspectiveAdjustment(
            normalizedText,
            perspective,
            rawVaderScore,
            fantasyAdjustment,
            matches,
            lexicon,
            appliedRules);
        var baseBeforeQuestion = Math.Clamp(
            rawVaderScore + fantasyAdjustment + preparation.EmojiAdjustment + perspectiveAdjustment,
            -1,
            1);
        var questionAdjustment = CalculateQuestionAdjustment(
            normalizedText,
            targetText,
            baseBeforeQuestion,
            appliedRules);
        var finalScore = Math.Round(Math.Clamp(baseBeforeQuestion + questionAdjustment, -1, 1), 3);

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
            perspectiveAdjustment,
            questionAdjustment,
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
        IReadOnlyList<SemanticTermMatch> matches,
        SentimentLexiconOptions lexicon,
        List<string> appliedRules)
    {
        var adjustment = 0.0;
        var hasExplicitNegative = fantasyAdjustment <= -0.20
                                  || matches.Any(match => match.Score < -0.40);
        var hasExplicitPositive = fantasyAdjustment >= 0.20
                                  || matches.Any(match => match.Score > 0.40);

        if (string.Equals(perspective, TargetPerspectives.OpponentRoster, StringComparison.OrdinalIgnoreCase))
        {
            var hasThreatLanguage = ContainsAny(normalizedText, lexicon.PerspectivePatterns.OpponentThreat)
                                    || Regex.IsMatch(
                                        normalizedText,
                                        @"\b(?:playing against|played against|facing|against me)\b",
                                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                                    && Regex.IsMatch(
                                        normalizedText,
                                        @"\b(?:wish|regret|hate|over|done|rip|lost|toast)\b",
                                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (hasThreatLanguage && !hasExplicitNegative)
            {
                adjustment = rawVaderScore < -0.10 ? 0.75 : 0.55;
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

    private static string BuildPhrasePattern(string normalizedPhrase)
    {
        return $@"(?<![a-z0-9]){Regex.Escape(normalizedPhrase).Replace("\\ ", "\\s+")}(?![a-z0-9])";
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
        var normalized = Regex.Replace(lowered, @"[^a-z0-9']+", " ");
        return Regex.Replace(normalized, @"\s+", " ").Trim();
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
    double PerspectiveAdjustment,
    double QuestionAdjustment,
    double FinalScore,
    IReadOnlyList<string> MatchedPositiveTerms,
    IReadOnlyList<string> MatchedNegativeTerms,
    IReadOnlyList<string> MatchedPhrases,
    IReadOnlyList<string> MatchedEmoji,
    IReadOnlyList<string> AppliedRules);

public sealed record SentimentResult(double Score, string Category);
