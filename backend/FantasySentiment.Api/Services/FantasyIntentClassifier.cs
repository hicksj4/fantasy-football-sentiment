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
        if (string.IsNullOrWhiteSpace(text))
        {
            return FantasyIntents.General;
        }

        var lexicon = options.CurrentValue;
        var normalizedFullText = Normalize(text);
        var normalizedTargetText = string.IsNullOrWhiteSpace(targetText)
            ? normalizedFullText
            : Normalize(targetText);
        var alias = Normalize(mention.MatchedAlias);
        var playerName = Normalize(mention.PlayerName);
        if (IsStartChoice(normalizedTargetText, alias, playerName))
        {
            return FantasyIntents.Start;
        }

        if (TryClassifyScopedAction(normalizedFullText, alias, playerName, out var scopedIntent))
        {
            return scopedIntent;
        }

        var context = BuildContext(normalizedTargetText, alias, playerName);

        if (Regex.IsMatch(
                context,
                @"\bwelcome\s+to\s+the\s+waiver\s+wire\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return FantasyIntents.Drop;
        }

        if (ContainsAny(context, lexicon.Injury))
        {
            return FantasyIntents.Injury;
        }

        if (ContainsAny(context, lexicon.Drop))
        {
            return FantasyIntents.Drop;
        }

        if (ContainsAny(context, lexicon.Add))
        {
            return FantasyIntents.Add;
        }

        if (ContainsAny(context, lexicon.Start))
        {
            return FantasyIntents.Start;
        }

        if (ContainsAny(context, lexicon.Sit))
        {
            return FantasyIntents.Sit;
        }

        if (ContainsAny(context, lexicon.Buy))
        {
            return FantasyIntents.Buy;
        }

        if (ContainsAny(context, lexicon.Sell))
        {
            return FantasyIntents.Sell;
        }

        if (ContainsAny(context, lexicon.Hold))
        {
            return FantasyIntents.Hold;
        }

        if (ContainsAny(context, lexicon.Panic))
        {
            return FantasyIntents.Panic;
        }

        if (ContainsAny(context, lexicon.Hype))
        {
            return FantasyIntents.Hype;
        }

        return FantasyIntents.General;
    }

    private static bool TryClassifyScopedAction(
        string normalized,
        string alias,
        string playerName,
        out string intent)
    {
        var dropFor = Regex.Match(
            normalized,
            @"\bdrop\s+(?<left>.+?)\s+for\s+(?<right>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (dropFor.Success)
        {
            var left = dropFor.Groups["left"].Value;
            var right = dropFor.Groups["right"].Value;
            if (ContainsMention(left, alias, playerName))
            {
                intent = FantasyIntents.Drop;
                return true;
            }

            if (ContainsMention(right, alias, playerName))
            {
                intent = FantasyIntents.Add;
                return true;
            }
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
                intent = FantasyIntents.Sit;
                return true;
            }

            if (ContainsMention(right, alias, playerName))
            {
                intent = FantasyIntents.Start;
                return true;
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
                intent = FantasyIntents.Sit;
                return true;
            }

            if (ContainsMention(right, alias, playerName))
            {
                intent = FantasyIntents.Start;
                return true;
            }
        }

        var startNot = Regex.Match(
            normalized,
            @"\b(?:starting|start)\s+(?<left>.+?)\s+not\s+(?<right>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (startNot.Success)
        {
            var left = startNot.Groups["left"].Value;
            var right = startNot.Groups["right"].Value;
            if (ContainsMention(left, alias, playerName))
            {
                intent = FantasyIntents.Start;
                return true;
            }

            if (ContainsMention(right, alias, playerName))
            {
                intent = FantasyIntents.Sit;
                return true;
            }
        }

        intent = FantasyIntents.General;
        return false;
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
}
