using System.Globalization;
using System.Text.RegularExpressions;
using FantasySentiment.Api.Domain;

namespace FantasySentiment.Api.Services;

public sealed class TargetContextBuilder(MentionContextExtractor fallbackExtractor)
{
    private static readonly Regex SentenceRegex = new(
        @"[^.!?\r\n]+(?:[.!?]+|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StrongClauseSeparatorRegex = new(
        @"\s+(?:but|however|though|while|whereas|yet)\s+|\s+and\s+(?=is\s+facing\b)|;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SoftAndSeparatorRegex = new(
        @"\s+and\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex IndependentStatementRegex = new(
        @"\b(?:is|are|was|were|look|looks|looked|has|have|had|got|gets|went|goes|sucks?|cooked|elite|great|awful|bad|good|solid|washed|terrible|incredible)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex OpponentRosterRegex = new(
        @"\bmy opponent(?:'s)?\s+(?:has|had|started|starts|played|plays|is starting|was starting|rolled out)\b|\b(?:opp|opponent)\b|\b(?:facing|faced)\b|\b(?:play|playing|played)\s+against\b|\bagainst me\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex OwnRosterRegex = new(
        @"\bas\s+(?:a|an)\b.{0,80}\bowner\b|\b(?:i|we)\s+(?:own|owning|owned|started|starting|start|played|play|rolled out|have|had|got|put|benched|benching|dropped|drafted|draft|chose|took)\b|\b(?:i've|ive|we've|weve)\s+got\b|\bowning\b|\bguess\s+who\s+(?:started|played)\b|\btaking\b.{0,100}\bover\b|\b(?:should\s+have|should've|shouldve|needed\s+to|shoulda)\s+started\b|\bon\s+my\s+bench\b|\bin\s+my\s+lineup\b|\b(?:my|our)\b.{0,80}\b(?:stack|team|room)\b|\bsave\s+me\s+from\b|\bgimme\b.{0,30}\bpoints\b|\b(?:waste\s+of\s+a\s+pick|draft\s+pick)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SharedComparisonRegex = new(
        @"\b(?:taking|took|drafted|started|starting)\b.{1,100}\bover\b|\bcould\s+have\s+had\b.{1,140}\b(?:chose|took|drafted|started)\b|\b(?:should\s+have|should've|shouldve|needed\s+to|shoulda)\s+started\b.{1,120}\bover\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex OutcomeContinuationRegex = new(
        @"\b(?:fantasy|week|season|matchup|lineup|team|over|done|lost|toast|cooked|rip|gg|panic)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public ResolvedTargetContext Build(
        string threadTitle,
        string? parentComment,
        string commentBody,
        ResolvedCommentTarget target,
        IReadOnlyList<ResolvedCommentTarget>? siblingTargets = null)
    {
        var trimmedCurrent = commentBody.Trim();
        var trimmedParent = string.IsNullOrWhiteSpace(parentComment) ? null : parentComment.Trim();
        var targets = siblingTargets ?? [target];
        var targetText = target.AttributionMethod == TargetAttributionMethods.Explicit
            ? ExtractExplicitTargetText(commentBody, target, targets)
            : BuildInheritedTargetText(trimmedParent, trimmedCurrent);
        if (target.AttributionMethod == TargetAttributionMethods.Explicit
            && IsChoiceAnswer(trimmedParent, targetText, target))
        {
            targetText = $"{trimmedParent} Selected answer: {target.PlayerName}";
        }

        var perspective = ResolvePerspective(targetText, target);

        return new ResolvedTargetContext(
            target,
            targetText,
            trimmedCurrent,
            trimmedParent,
            threadTitle.Trim(),
            perspective);
    }

    private string ExtractExplicitTargetText(
        string text,
        ResolvedCommentTarget target,
        IReadOnlyList<ResolvedCommentTarget> siblingTargets)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sentences = SplitSentences(text);
        if (sentences.Count == 0)
        {
            return fallbackExtractor.ExtractContext(text, target.ToMatch());
        }

        var mentionIndex = Math.Clamp(target.StartIndex, 0, Math.Max(0, text.Length - 1));
        var sentenceIndex = sentences.FindIndex(sentence => sentence.Contains(mentionIndex));
        if (sentenceIndex < 0)
        {
            return fallbackExtractor.ExtractContext(text, target.ToMatch());
        }

        var sentence = sentences[sentenceIndex];
        var targetsInSentence = siblingTargets
            .Where(item => item.Length > 0 && sentence.Contains(item.StartIndex))
            .ToList();
        var sharedStatement = IsSharedMultiTargetStatement(sentence.Text, targetsInSentence);
        var targetText = sentence.Text;
        if (!sharedStatement)
        {
            var targetClause = ExtractTargetClause(sentence, target, targetsInSentence);
            if (!string.IsNullOrWhiteSpace(targetClause))
            {
                targetText = targetClause;
            }
        }

        var parts = new List<string> { targetText.Trim() };
        var previousSentence = sentence;
        for (var nextIndex = sentenceIndex + 1; nextIndex < sentences.Count && parts.Count < 4; nextIndex++)
        {
            var nextSentence = sentences[nextIndex];
            if (!ShouldIncludeNextSentence(previousSentence, nextSentence, sharedStatement, targetsInSentence))
            {
                break;
            }

            parts.Add(nextSentence.Text);
            previousSentence = nextSentence;
        }

        return string.Join(' ', parts).Trim();
    }

    private static string BuildInheritedTargetText(string? parentComment, string currentComment)
    {
        if (string.IsNullOrWhiteSpace(parentComment))
        {
            return currentComment.Trim();
        }

        return $"{parentComment.Trim()} {currentComment.Trim()}".Trim();
    }

    private static string? ExtractTargetClause(
        TextSpan sentence,
        ResolvedCommentTarget target,
        IReadOnlyList<ResolvedCommentTarget> targetsInSentence)
    {
        var strongClauses = SplitBySeparator(sentence, StrongClauseSeparatorRegex);
        if (strongClauses.Count > 1)
        {
            return strongClauses.FirstOrDefault(clause => clause.Contains(target.StartIndex))?.Text.Trim();
        }

        var softAndClauses = SplitOnIndependentAnd(sentence, targetsInSentence);
        if (softAndClauses.Count > 1)
        {
            return softAndClauses.FirstOrDefault(clause => clause.Contains(target.StartIndex))?.Text.Trim();
        }

        return null;
    }

    private static IReadOnlyList<TextSpan> SplitBySeparator(TextSpan span, Regex separatorRegex)
    {
        var separators = separatorRegex.Matches(span.Text);
        if (separators.Count == 0)
        {
            return [span];
        }

        var parts = new List<TextSpan>();
        var partStart = 0;
        foreach (Match separator in separators)
        {
            AddPart(partStart, separator.Index);
            partStart = separator.Index + separator.Length;
        }

        AddPart(partStart, span.Text.Length);
        return parts;

        void AddPart(int start, int end)
        {
            if (end <= start)
            {
                return;
            }

            var raw = span.Text[start..end];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                return;
            }

            var leadingWhitespace = raw.Length - raw.TrimStart().Length;
            parts.Add(new TextSpan(
                span.StartIndex + start + leadingWhitespace,
                trimmed.Length,
                trimmed));
        }
    }

    private static IReadOnlyList<TextSpan> SplitOnIndependentAnd(
        TextSpan sentence,
        IReadOnlyList<ResolvedCommentTarget> targetsInSentence)
    {
        var separators = SoftAndSeparatorRegex.Matches(sentence.Text);
        if (separators.Count != 1 || targetsInSentence.Count < 2)
        {
            return [sentence];
        }

        var separator = separators[0];
        var left = MakePart(0, separator.Index);
        var right = MakePart(separator.Index + separator.Length, sentence.Text.Length);
        if (left is null || right is null)
        {
            return [sentence];
        }

        var leftTargets = targetsInSentence.Where(target => left.Contains(target.StartIndex)).ToList();
        var rightTargets = targetsInSentence.Where(target => right.Contains(target.StartIndex)).ToList();
        if (leftTargets.Count != 1
            || rightTargets.Count != 1
            || leftTargets[0].PlayerId == rightTargets[0].PlayerId
            || !IndependentStatementRegex.IsMatch(left.Text)
            || !IndependentStatementRegex.IsMatch(right.Text))
        {
            return [sentence];
        }

        return [left, right];

        TextSpan? MakePart(int start, int end)
        {
            if (end <= start)
            {
                return null;
            }

            var raw = sentence.Text[start..end];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            var leadingWhitespace = raw.Length - raw.TrimStart().Length;
            return new TextSpan(
                sentence.StartIndex + start + leadingWhitespace,
                trimmed.Length,
                trimmed);
        }
    }

    private static bool IsSharedMultiTargetStatement(
        string sentence,
        IReadOnlyList<ResolvedCommentTarget> targetsInSentence)
    {
        if (targetsInSentence.Count < 2)
        {
            return false;
        }

        if (SharedComparisonRegex.IsMatch(sentence))
        {
            return true;
        }

        return OpponentRosterRegex.IsMatch(sentence)
               && !OwnRosterRegex.IsMatch(sentence);
    }

    private static bool IsChoiceAnswer(
        string? parentComment,
        string targetText,
        ResolvedCommentTarget target)
    {
        if (string.IsNullOrWhiteSpace(parentComment)
            || string.IsNullOrWhiteSpace(targetText)
            || targetText.Length > 80
            || !Regex.IsMatch(parentComment, @"\b(?:or|vs|versus)\b|/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        var aliases = TargetAliases(target);
        var answerOnly = aliases.Any(alias =>
            Regex.IsMatch(
                targetText,
                $@"^\s*{Regex.Escape(alias).Replace("\\ ", "\\s+")}\s*[.!?]*\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        return answerOnly && aliases.Any(alias => ResolveAliasInText(parentComment, alias));
    }

    private static bool ShouldIncludeNextSentence(
        TextSpan previousSentence,
        TextSpan nextSentence,
        bool sharedStatement,
        IReadOnlyList<ResolvedCommentTarget> targetsInPreviousSentence)
    {
        var next = nextSentence.Text.Trim();
        if (next.Length == 0)
        {
            return false;
        }

        if (IsLikelyContinuation(next))
        {
            return true;
        }

        if (IsEllipsisContinuation(previousSentence.Text, next)
            || IsRosterReplacementContinuation(next))
        {
            return true;
        }

        if (targetsInPreviousSentence.Count == 1 && IsSingleTargetActionContinuation(next))
        {
            return true;
        }

        return sharedStatement
               && targetsInPreviousSentence.Count >= 2
               && next.Length <= 180
               && (OutcomeContinuationRegex.IsMatch(next) || ContainsEmojiLikeSymbol(next));
    }

    private static bool ResolveAliasInText(string text, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return false;
        }

        var escaped = Regex.Escape(alias.Trim());
        escaped = Regex.Replace(escaped, @"\\\s+", @"\s+");
        return Regex.IsMatch(
            text,
            $@"(?<![A-Za-z0-9]){escaped}(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string ResolvePerspective(string targetText, ResolvedCommentTarget target)
    {
        var aliases = TargetAliases(target);
        if (aliases.Any(alias => HasOwnRosterPerspective(targetText, alias)))
        {
            return TargetPerspectives.OwnRoster;
        }

        if (aliases.Any(alias => HasOpponentRosterPerspective(targetText, alias)))
        {
            return TargetPerspectives.OpponentRoster;
        }

        return TargetPerspectives.Unknown;
    }

    private static bool HasOpponentRosterPerspective(string text, string alias)
    {
        if (!ResolveAliasInText(text, alias))
        {
            return false;
        }

        var escapedAlias = Regex.Escape(alias.Trim());
        escapedAlias = Regex.Replace(escapedAlias, @"\\\s+", @"\s+");
        var aliasPattern = $@"(?<![A-Za-z0-9]){escapedAlias}(?![A-Za-z0-9])";
        return OpponentRosterRegex.IsMatch(text)
               || Regex.IsMatch(
                   text,
                   $@"\b(?:facing|faced)\s+{aliasPattern}\b|\b(?:i\s+am|i'm|im)\s+facing\s+{aliasPattern}\b|\bopp(?:onent)?\s+(?:has\s+)?{aliasPattern}\b|\b(?:her|his|their)\s+{aliasPattern}\b|{aliasPattern}.{{0,70}}\b(?:killing|destroying|burying|booming|outscoring|fucking)\s+me\b|{aliasPattern}.{{0,70}}\b(?:ended|ruined|buried|boomed)\s+my\s+(?:week|matchup)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasOwnRosterPerspective(string text, string alias)
    {
        if (!ResolveAliasInText(text, alias))
        {
            return false;
        }

        var escapedAlias = Regex.Escape(alias.Trim());
        escapedAlias = Regex.Replace(escapedAlias, @"\\\s+", @"\s+");
        var aliasPattern = $@"(?<![A-Za-z0-9]){escapedAlias}(?![A-Za-z0-9])";
        return OwnRosterRegex.IsMatch(text)
               || Regex.IsMatch(
                   text,
                   $@"\bas\s+(?:a|an)\b.{{0,80}}{aliasPattern}.{{0,80}}\bowner\b|\bto\s+be\s+(?:a|an)\b.{{0,80}}{aliasPattern}.{{0,80}}\bowner\b|\b(?:i|we)\s+(?:own|owned|started|starting|start|played|play|rolled\s+out|have|had|got|put|benched|benching|dropped|drafted|draft|chose|took)\b.{{0,80}}{aliasPattern}|\b(?:started|starting|benched|benching|dropped|put)\b.{{0,80}}{aliasPattern}|\bowning\b.{{0,80}}{aliasPattern}|\b(?:i've|ive|we've|weve)\s+got\b.{{0,80}}{aliasPattern}|\bhave\b.{{0,80}}{aliasPattern}\b|\bguess\s+who\s+(?:started|played)\b.{{0,80}}{aliasPattern}|\bmy\s+(?:(?:qb|rb|wr|te|flex|starter|bench|lineup|best\s+receiver)\s+)?{aliasPattern}|{aliasPattern}.{{0,70}}\b(?:on\s+my\s+bench|in\s+my\s+lineup|on\s+my\s+team|is\s+my\s+(?:qb|rb|wr|te|flex|best\s+receiver|receiver|player|boi|boy))\b|{aliasPattern}\s+is\s+my\s+best\s+receiver\b|\b(?:should\s+have|should've|shouldve|needed\s+to|shoulda)\s+started\b.{{0,100}}{aliasPattern}|\bi\s+chose\b.{{0,80}}{aliasPattern}|\bmy\b.{{0,80}}{aliasPattern}.{{0,50}}\b(?:stack|room|stonks)\b|\b(?:that'?s|thats)\s+my\s+(?:boi|boy|guy|dude)\s+{aliasPattern}\b|{aliasPattern}.{{0,80}}\b(?:waste\s+of\s+a\s+pick|draft\s+pick)\b|{aliasPattern}.{{0,30}}\b(?:save\s+me|gimme\b.{{0,30}}\bpoints)\b|{aliasPattern}.{{0,120}}\bi\s+have\b.{{0,80}}\b(?:replace\s+him|replace\s+them|replace\s+that)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static IReadOnlyList<string> TargetAliases(ResolvedCommentTarget target)
    {
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
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<TextSpan> SplitSentences(string text)
    {
        var sentences = SentenceRegex
            .Matches(text)
            .Select(match =>
            {
                var trimmed = match.Value.Trim();
                var leadingWhitespace = match.Value.Length - match.Value.TrimStart().Length;
                return new TextSpan(
                    match.Index + leadingWhitespace,
                    trimmed.Length,
                    trimmed);
            })
            .Where(sentence => sentence.Text.Length > 0)
            .ToList();

        return sentences.Count == 0
            ? [new TextSpan(0, text.Trim().Length, text.Trim())]
            : sentences;
    }

    private static bool IsLikelyContinuation(string sentence)
    {
        var lower = sentence.Trim().ToLowerInvariant();
        return lower.StartsWith("he's ")
               || lower.StartsWith("he is ")
               || lower.StartsWith("he ")
               || lower.StartsWith("she's ")
               || lower.StartsWith("she is ")
               || lower.StartsWith("she ")
               || lower.StartsWith("they're ")
               || lower.StartsWith("they are ")
               || lower.StartsWith("they ")
               || lower.StartsWith("had him ")
               || lower.StartsWith("had her ")
               || lower.StartsWith("had them ")
               || lower.StartsWith("feeling ")
               || lower.StartsWith("first time ")
               || lower.StartsWith("turned out ")
               || lower.StartsWith("still ")
               || lower.StartsWith("also ")
               || lower.StartsWith("that ")
               || lower.StartsWith("this ");
    }

    private static bool IsEllipsisContinuation(string previous, string next)
    {
        var trimmedPrevious = previous.TrimEnd();
        if (!Regex.IsMatch(trimmedPrevious, @"(?:\.{2,}|…+)$", RegexOptions.CultureInvariant))
        {
            return false;
        }

        return next.Length <= 120
               && !char.IsUpper(next[0]);
    }

    private static bool IsRosterReplacementContinuation(string sentence)
    {
        return Regex.IsMatch(
            sentence,
            @"\b(?:bright\s+side\b)?.{0,80}\bi\s+have\b.{0,80}\breplace\s+(?:him|them|that)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsSingleTargetActionContinuation(string sentence)
    {
        return Regex.IsMatch(
            sentence,
            @"^(?:i(?:'m| am)|we(?:'re| are))\s+(?:selling|dropping|holding|buying|starting|benching|sitting|adding)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ContainsEmojiLikeSymbol(string text)
    {
        return text.Any(character =>
        {
            var category = char.GetUnicodeCategory(character);
            return category is UnicodeCategory.OtherSymbol or UnicodeCategory.Surrogate;
        });
    }

    private sealed record TextSpan(int StartIndex, int Length, string Text)
    {
        public int EndIndex => StartIndex + Length;

        public bool Contains(int index)
        {
            return index >= StartIndex && index < EndIndex;
        }
    }
}

public sealed record ResolvedTargetContext(
    ResolvedCommentTarget Target,
    string TargetText,
    string CurrentComment,
    string? ParentComment,
    string ThreadTitle,
    string Perspective);
