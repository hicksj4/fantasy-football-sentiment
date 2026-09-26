using System.Text.RegularExpressions;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Services;

public sealed class FantasyIntentClassifier(IOptionsMonitor<IntentLexiconOptions> options)
{
    public string Classify(string text, PlayerMentionMatch mention)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return FantasyIntents.General;
        }

        var lexicon = options.CurrentValue;
        var normalized = Normalize(text);
        var alias = Normalize(mention.MatchedAlias);
        var playerName = Normalize(mention.PlayerName);
        var context = BuildContext(normalized, alias, playerName);

        var dropFor = Regex.Match(normalized, @"\bdrop\s+(?<left>.+?)\s+for\s+(?<right>.+?)(?:[?.!,]|$)");
        if (dropFor.Success)
        {
            var left = dropFor.Groups["left"].Value;
            var right = dropFor.Groups["right"].Value;
            if (ContainsMention(left, alias, playerName))
            {
                return FantasyIntents.Drop;
            }

            if (ContainsMention(right, alias, playerName))
            {
                return FantasyIntents.Add;
            }
        }

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
        return value.Contains(alias, StringComparison.OrdinalIgnoreCase)
               || value.Contains(playerName, StringComparison.OrdinalIgnoreCase);
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

    private static string Normalize(string value)
    {
        var lowered = value.ToLowerInvariant();
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
