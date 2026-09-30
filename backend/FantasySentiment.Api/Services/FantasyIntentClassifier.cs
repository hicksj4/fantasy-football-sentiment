using System.Text.RegularExpressions;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Services;

public sealed class FantasyIntentClassifier(IOptionsMonitor<IntentLexiconOptions> options)
{
    public string Classify(string text, PlayerMentionMatch mention)
    {
        return Classify(text, text, mention);
    }

    public string Classify(string text, string targetText, PlayerMentionMatch mention)
    {
        return ClassifyAll(text, targetText, mention).LegacyIntent;
    }

    public FantasySemanticClassification ClassifyAll(string text, PlayerMentionMatch mention)
    {
        return ClassifyAll(text, text, mention);
    }

    public FantasySemanticClassification ClassifyAll(string text, string targetText, PlayerMentionMatch mention)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return FantasySemanticClassification.Empty;
        }

        var lexicon = options.CurrentValue;
        var normalizedFullText = Normalize(text);
        var normalizedTargetText = string.IsNullOrWhiteSpace(targetText)
            ? normalizedFullText
            : Normalize(targetText);
        var alias = Normalize(mention.MatchedAlias);
        var playerName = Normalize(mention.PlayerName);
        var actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var signals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (IsStartChoice(normalizedTargetText, alias, playerName))
        {
            actions.Add(FantasyActions.Start);
        }

        ClassifyScopedActions(normalizedFullText, alias, playerName, actions);

        var context = BuildContext(normalizedTargetText, alias, playerName);

        if (ContainsAny(context, lexicon.Injury))
        {
            signals.Add(FantasyDiscussionSignals.Injury);
        }

        if (ContainsAny(context, lexicon.Drop))
        {
            actions.Add(FantasyActions.Drop);
        }

        if (!actions.Contains(FantasyActions.Drop)
            && ContainsAny(context, lexicon.Add))
        {
            actions.Add(FantasyActions.Add);
        }

        if (ContainsAny(context, lexicon.Start))
        {
            actions.Add(FantasyActions.Start);
        }

        if (ContainsAny(context, lexicon.Sit))
        {
            actions.Add(FantasyActions.Sit);
        }

        if (ContainsAny(context, lexicon.Buy))
        {
            actions.Add(FantasyActions.Buy);
        }

        if (ContainsAny(context, lexicon.Sell))
        {
            actions.Add(FantasyActions.Sell);
        }

        if (ContainsAny(context, lexicon.Hold))
        {
            actions.Add(FantasyActions.Hold);
        }

        return new FantasySemanticClassification(
            OrderLabels(actions, FantasyActions.All),
            OrderLabels(signals, FantasyDiscussionSignals.All));
    }

    private static void ClassifyScopedActions(
        string normalized,
        string alias,
        string playerName,
        HashSet<string> actions)
    {
        var mentionPattern = MentionPattern(alias, playerName);
        var dropFor = Regex.Match(
            normalized,
            @"\b(?:drop|dropping|dropped)\s+(?<left>.+?)\s+for\s+(?<right>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (dropFor.Success)
        {
            var left = dropFor.Groups["left"].Value;
            var right = dropFor.Groups["right"].Value;
            if (ContainsMention(left, alias, playerName))
            {
                actions.Add(FantasyActions.Drop);
            }

            if (ContainsMention(right, alias, playerName))
            {
                actions.Add(FantasyActions.Add);
            }
        }

        if (Regex.IsMatch(
                normalized,
                $@"\b(?:(?:i|we)\s+(?:am\s+|are\s+)?|i'm\s+|we're\s+)?(?:already\s+)?(?:drop|dropping|dropped)\s+{mentionPattern}\b|\b(?:drop|dropping|dropped)\s+(?:the\s+)?(?:shit|fuck)\s+(?:outta|out\s+of)\s+{mentionPattern}\b|\b(?:gonna|going\s+to|about\s+to|can't\s+wait\s+to|cant\s+wait\s+to)\s+drop\s+{mentionPattern}\b|{mentionPattern}\b.{{0,35}}\b(?:is|was|looks?|seems?)\s+(?:a\s+)?(?:drop|droppable)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            actions.Add(FantasyActions.Drop);
        }

        if (Regex.IsMatch(
                normalized,
                $@"\b(?:(?:i|we)\s+(?:am\s+|are\s+)?|i'm\s+|we're\s+)?(?:sell|selling|sold)\s+{mentionPattern}\b|{mentionPattern}\b.{{0,45}}\b(?:is|was|looks?|seems?)\s+(?:a\s+)?sell\b|{mentionPattern}\b.{{0,80}}\b(?:i'm|im|i\s+am|we're|were|we\s+are)\s+selling\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            actions.Add(FantasyActions.Sell);
        }

        if (Regex.IsMatch(
                normalized,
                $@"(?:{mentionPattern}\s+(?:to|straight\s+to)\s+(?:the\s+)?(?:waiver\s+wire|waivers)\b|\bsend\s+{mentionPattern}\s+to\s+(?:the\s+)?(?:waiver\s+wire|waivers)\b|{mentionPattern}\b.{{0,80}}\bwelcome\s+to\s+(?:the\s+)?(?:waiver\s+wire|waivers)\b|{mentionPattern}\b.{{0,40}}\b(?:is|looks?|seems?)\s+(?:a\s+)?(?:waiver\s+player|waiver\s+wire\s+player)\b)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            actions.Add(FantasyActions.Drop);
        }

        if (Regex.IsMatch(
                normalized,
                $@"\b(?:pick\s+up|pickup|grab|claim|scoop)\s+{mentionPattern}\b.{{0,40}}\b(?:off|on|from)\s+waivers?\b|\b(?:best|top)\s+waiver\s+(?:add|pickup)\b.{{0,80}}{mentionPattern}\b|{mentionPattern}\b.{{0,80}}\b(?:best|top)\s+waiver\s+(?:add|pickup)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            actions.Add(FantasyActions.Add);
        }

        if (Regex.IsMatch(
                normalized,
                $@"\b(?:started|starting)\s+{mentionPattern}\b.{{0,40}}\bama\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            actions.Add(FantasyActions.Start);
        }

        if (Regex.IsMatch(
                normalized,
                $@"\b(?:benched|benching)\s+{mentionPattern}\b.{{0,40}}\bama\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            actions.Add(FantasyActions.Sit);
        }

        var sitFor = Regex.Match(
            normalized,
            @"\b(?:benched|benching|bench|sitting|sit)\s+(?<left>.+?)\s+for\s+(?<right>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (sitFor.Success)
        {
            var left = sitFor.Groups["left"].Value;
            var right = sitFor.Groups["right"].Value;
            if (ContainsMention(left, alias, playerName))
            {
                actions.Add(FantasyActions.Sit);
            }

            if (ContainsMention(right, alias, playerName))
            {
                actions.Add(FantasyActions.Start);
            }
        }

        var sitNot = Regex.Match(
            normalized,
            @"\b(?:benched|benching|bench|sitting|sit)\s+(?<left>.+?)\s+not\s+(?<right>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (sitNot.Success)
        {
            var left = sitNot.Groups["left"].Value;
            var right = sitNot.Groups["right"].Value;
            if (ContainsMention(left, alias, playerName))
            {
                actions.Add(FantasyActions.Sit);
            }

            if (ContainsMention(right, alias, playerName))
            {
                actions.Add(FantasyActions.Start);
            }
        }

        var startNot = Regex.Match(
            normalized,
            @"\b(?:starting|start)\s+(?<left>.+?)\s+not\s+(?<right>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (startNot.Success
            && !Regex.IsMatch(
                normalized,
                @"\bstarting\s+to\s+(?:think|believe|wonder|worry)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var left = startNot.Groups["left"].Value;
            var right = startNot.Groups["right"].Value;
            if (ContainsMention(left, alias, playerName))
            {
                actions.Add(FantasyActions.Start);
            }

            if (ContainsMention(right, alias, playerName))
            {
                actions.Add(FantasyActions.Sit);
            }
        }
    }

    private static bool IsStartChoice(string normalized, string alias, string playerName)
    {
        if (!ContainsMention(normalized, alias, playerName)
            || !Regex.IsMatch(normalized, @"\b(?:or|vs|versus)\b|/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        return normalized.Length <= 120
               || Regex.IsMatch(
                   normalized,
                   @"\b(?:start|starting|flex|lineup|who)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string BuildContext(string normalized, string alias, string playerName)
    {
        var index = normalized.IndexOf(alias, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            index = normalized.IndexOf(playerName, StringComparison.OrdinalIgnoreCase);
        }

        if (index < 0)
        {
            return normalized;
        }

        var start = Math.Max(0, index - 80);
        var length = Math.Min(normalized.Length - start, alias.Length + 160);
        return normalized.Substring(start, length);
    }

    private static bool ContainsMention(string value, string alias, string playerName)
    {
        return IsTermMatch(value, alias)
               || IsTermMatch(value, playerName);
    }

    private static string MentionPattern(string alias, string playerName)
    {
        var patterns = new[] { alias, playerName }
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(term => Regex.Escape(term).Replace("\\ ", "\\s+"))
            .ToArray();

        return patterns.Length == 0
            ? @"(?!)"
            : $@"(?<![a-z0-9])(?:{string.Join('|', patterns)})(?![a-z0-9])";
    }

    private static bool ContainsAny(string value, IEnumerable<string> terms)
    {
        return terms.Any(term =>
        {
            var normalizedTerm = Normalize(term);
            if (normalizedTerm.Length == 0)
            {
                return false;
            }

            var pattern = $@"(?<![a-z0-9]){Regex.Escape(normalizedTerm).Replace("\\ ", "\\s+")}(?![a-z0-9])";
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        });
    }

    private static bool IsTermMatch(string value, string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        var pattern = $@"(?<![a-z0-9]){Regex.Escape(term).Replace("\\ ", "\\s+")}(?![a-z0-9])";
        return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Normalize(string value)
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

    private static IReadOnlyList<string> OrderLabels(
        IReadOnlySet<string> labels,
        IEnumerable<string> preferredOrder)
    {
        return preferredOrder
            .Where(labels.Contains)
            .ToList();
    }
}

public sealed record FantasySemanticClassification(
    IReadOnlyList<string> FantasyActions,
    IReadOnlyList<string> DiscussionSignals)
{
    public static FantasySemanticClassification Empty { get; } = new([], []);

    public string LegacyIntent =>
        FantasyActions.FirstOrDefault()
        ?? DiscussionSignals.FirstOrDefault()
        ?? FantasyIntents.General;
}
