using System.Text.RegularExpressions;
using FantasySentiment.Api.Domain;

namespace FantasySentiment.Api.Services;

public sealed class PlayerMentionDetector
{
    private static readonly Regex LastNameRegex = new(@"\b(?<last>[A-Za-z][A-Za-z'.-]+)$", RegexOptions.Compiled);
    private static readonly HashSet<string> AmbiguousSingleWordAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "brown",
        "johnson",
        "jones",
        "moore",
        "smith",
        "thomas",
        "williams",
        "wilson"
    };

    private static readonly HashSet<string> RiskySingleWordAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "love"
    };

    private static readonly Regex RiskyAliasFollowingContextRegex = new(
        @"^(?:is|was|looks?|looked|has|had|will|should|can|could|needs?|throws?|throwing|played|playing|started|starting|cooked|bad|good|great|awful|solid|washed|stinks|ok(?:ay)?)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public IReadOnlyList<PlayerMentionMatch> DetectMentions(string text, IEnumerable<Player> players)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var candidates = BuildCandidates(players);
        var aliasesByKey = candidates
            .GroupBy(candidate => NormalizeAlias(candidate.Alias), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(candidate => candidate.Player.Id).Distinct().Count() == 1)
            .Select(group => group.OrderByDescending(candidate => candidate.Alias.Length).First())
            .Where(candidate => !IsUnsafeSingleWordAlias(candidate.Alias))
            .OrderByDescending(candidate => candidate.Alias.Length)
            .ToList();

        var matches = new List<PlayerMentionMatch>();
        var seenPlayerIds = new HashSet<int>();

        foreach (var candidate in aliasesByKey)
        {
            if (seenPlayerIds.Contains(candidate.Player.Id))
            {
                continue;
            }

            var pattern = BuildAliasPattern(candidate.Alias);
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            if (IsRiskySingleWordAlias(candidate.Alias) && !HasRiskyAliasEvidence(text, match))
            {
                continue;
            }

            seenPlayerIds.Add(candidate.Player.Id);
            matches.Add(new PlayerMentionMatch(
                candidate.Player.Id,
                candidate.Player.Name,
                candidate.Player.Team,
                candidate.Player.Position,
                candidate.Alias,
                match.Index,
                match.Length));
        }

        return matches
            .OrderBy(match => match.StartIndex)
            .ToList();
    }

    private static List<AliasCandidate> BuildCandidates(IEnumerable<Player> players)
    {
        var candidates = new List<AliasCandidate>();
        foreach (var player in players)
        {
            AddCandidate(candidates, player, player.Name);

            var lastNameMatch = LastNameRegex.Match(player.Name);
            if (lastNameMatch.Success)
            {
                AddCandidate(candidates, player, lastNameMatch.Groups["last"].Value);
            }

            foreach (var alias in player.Aliases)
            {
                AddCandidate(candidates, player, alias.Alias);
            }
        }

        return candidates;
    }

    private static void AddCandidate(List<AliasCandidate> candidates, Player player, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return;
        }

        candidates.Add(new AliasCandidate(player, alias.Trim()));
    }

    private static string BuildAliasPattern(string alias)
    {
        var escaped = Regex.Escape(alias.Trim());
        escaped = Regex.Replace(escaped, @"\\\s+", @"\s+");
        return $@"(?<![A-Za-z0-9]){escaped}(?![A-Za-z0-9])";
    }

    private static bool IsUnsafeSingleWordAlias(string alias)
    {
        var normalized = NormalizeAlias(alias);
        return !normalized.Contains(' ') && AmbiguousSingleWordAliases.Contains(normalized);
    }

    private static bool IsRiskySingleWordAlias(string alias)
    {
        var normalized = NormalizeAlias(alias);
        return !normalized.Contains(' ') && RiskySingleWordAliases.Contains(normalized);
    }

    private static bool HasRiskyAliasEvidence(string text, Match match)
    {
        if (match.Value.Length == 0 || !char.IsUpper(match.Value[0]))
        {
            return false;
        }

        var followingText = text[(match.Index + match.Length)..].TrimStart();
        return RiskyAliasFollowingContextRegex.IsMatch(followingText);
    }

    public static string NormalizeAlias(string value)
    {
        var normalized = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
        return Regex.Replace(normalized, @"\s+", " ");
    }

    private sealed record AliasCandidate(Player Player, string Alias);
}

public sealed record PlayerMentionMatch(
    int PlayerId,
    string PlayerName,
    string Team,
    string Position,
    string MatchedAlias,
    int StartIndex,
    int Length);
