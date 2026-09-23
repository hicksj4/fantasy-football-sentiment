using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace FantasySentiment.Api.Services;

public sealed class AnalyticsQueryService(FantasySentimentDbContext db)
{
    public async Task<IReadOnlyList<PlayerListItemDto>> SearchPlayersAsync(
        string? search,
        int limit,
        CancellationToken cancellationToken)
    {
        var players = await db.Players
            .AsNoTracking()
            .Include(player => player.Aliases)
            .Where(player => player.IsActive)
            .OrderBy(player => player.Name)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = PlayerMentionDetector.NormalizeAlias(search);
            players = players
                .Where(player =>
                    PlayerMentionDetector.NormalizeAlias(player.Name).Contains(normalizedSearch)
                    || player.Team.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || player.Position.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || player.Aliases.Any(alias => PlayerMentionDetector.NormalizeAlias(alias.Alias).Contains(normalizedSearch)))
                .ToList();
        }

        return players
            .Take(Math.Clamp(limit, 1, 100))
            .Select(ToListItem)
            .ToList();
    }

    public async Task<PlayerMetricsDto?> GetPlayerAsync(int id, CancellationToken cancellationToken)
    {
        var player = await db.Players
            .AsNoTracking()
            .Include(item => item.Aliases)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (player is null)
        {
            return null;
        }

        var latest = await db.PlayerSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.PlayerId == id)
            .OrderByDescending(snapshot => snapshot.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);

        return ToMetrics(player, latest);
    }

    public async Task<IReadOnlyList<PlayerHistoryPointDto>> GetPlayerHistoryAsync(
        int id,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        var start = DateTimeOffset.UtcNow - window;
        return await db.PlayerSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.PlayerId == id && snapshot.Timestamp >= start)
            .OrderBy(snapshot => snapshot.Timestamp)
            .Select(snapshot => new PlayerHistoryPointDto(
                snapshot.Timestamp,
                snapshot.MentionCount,
                snapshot.SentimentScore,
                snapshot.BuzzScore))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DashboardPlayerDto>> GetDashboardRowsAsync(
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        var start = DateTimeOffset.UtcNow - window;
        var players = await db.Players
            .AsNoTracking()
            .Where(player => player.IsActive)
            .ToListAsync(cancellationToken);

        var mentionStats = await db.PlayerMentions
            .AsNoTracking()
            .Where(mention => mention.CreatedAt >= start)
            .GroupBy(mention => mention.PlayerId)
            .Select(group => new
            {
                PlayerId = group.Key,
                Mentions = group.Count(),
                Sentiment = group.Average(mention => mention.SentimentScore),
                AddCount = group.Count(mention => mention.Intent == FantasyIntents.Add),
                DropCount = group.Count(mention => mention.Intent == FantasyIntents.Drop),
                StartCount = group.Count(mention => mention.Intent == FantasyIntents.Start),
                SitCount = group.Count(mention => mention.Intent == FantasyIntents.Sit),
                BuyCount = group.Count(mention => mention.Intent == FantasyIntents.Buy),
                SellCount = group.Count(mention => mention.Intent == FantasyIntents.Sell),
                HypeCount = group.Count(mention => mention.Intent == FantasyIntents.Hype),
                PanicCount = group.Count(mention => mention.Intent == FantasyIntents.Panic)
            })
            .ToDictionaryAsync(item => item.PlayerId, cancellationToken);

        var snapshots = await db.PlayerSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.Timestamp >= start)
            .OrderBy(snapshot => snapshot.Timestamp)
            .ToListAsync(cancellationToken);

        var snapshotGroups = snapshots
            .GroupBy(snapshot => snapshot.PlayerId)
            .ToDictionary(group => group.Key, group => new
            {
                First = group.First(),
                Last = group.Last()
            });

        var rows = new List<DashboardPlayerDto>();
        foreach (var player in players)
        {
            mentionStats.TryGetValue(player.Id, out var mentions);
            snapshotGroups.TryGetValue(player.Id, out var groupedSnapshots);

            var latestSnapshot = groupedSnapshots?.Last;
            var firstSnapshot = groupedSnapshots?.First;
            var mentionCount = mentions?.Mentions ?? latestSnapshot?.MentionCount ?? 0;

            rows.Add(new DashboardPlayerDto(
                player.Id,
                player.Name,
                player.Team,
                player.Position,
                mentionCount,
                latestSnapshot?.BuzzScore ?? 1,
                Math.Round(mentions?.Sentiment ?? latestSnapshot?.SentimentScore ?? 0, 3),
                Math.Round((latestSnapshot?.SentimentScore ?? 0) - (firstSnapshot?.SentimentScore ?? 0), 3),
                mentions?.AddCount ?? latestSnapshot?.AddCount ?? 0,
                mentions?.DropCount ?? latestSnapshot?.DropCount ?? 0,
                mentions?.StartCount ?? latestSnapshot?.StartCount ?? 0,
                mentions?.SitCount ?? latestSnapshot?.SitCount ?? 0,
                mentions?.BuyCount ?? latestSnapshot?.BuyCount ?? 0,
                mentions?.SellCount ?? latestSnapshot?.SellCount ?? 0,
                mentions?.HypeCount ?? latestSnapshot?.HypeCount ?? 0,
                mentions?.PanicCount ?? latestSnapshot?.PanicCount ?? 0));
        }

        return rows;
    }

    public async Task<IReadOnlyList<TeamSummaryDto>> GetTeamSummariesAsync(
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        var rows = await GetDashboardRowsAsync(window, cancellationToken);
        return rows
            .GroupBy(row => row.Team)
            .Select(group =>
            {
                var players = group.ToList();
                var mentions = players.Sum(player => player.Mentions);
                return new TeamSummaryDto(
                    group.Key,
                    mentions,
                    mentions == 0 ? 0 : Math.Round(players.Sum(player => player.Sentiment * player.Mentions) / mentions, 3),
                    players.Count == 0 ? 1 : Math.Round(players.Max(player => player.Buzz), 2),
                    players.OrderByDescending(player => player.Mentions).FirstOrDefault()?.Name,
                    players.OrderByDescending(player => player.SentimentChange).FirstOrDefault()?.Name,
                    players.OrderByDescending(player => player.Buzz).FirstOrDefault()?.Name);
            })
            .OrderByDescending(team => team.Mentions)
            .ToList();
    }

    public async Task<TeamDetailDto?> GetTeamDetailAsync(
        string team,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        var rows = await GetDashboardRowsAsync(window, cancellationToken);
        var players = rows
            .Where(row => row.Team.Equals(team, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(row => row.Mentions)
            .ToList();

        if (players.Count == 0)
        {
            return null;
        }

        var mentions = players.Sum(player => player.Mentions);
        return new TeamDetailDto(
            players[0].Team,
            mentions,
            mentions == 0 ? 0 : Math.Round(players.Sum(player => player.Sentiment * player.Mentions) / mentions, 3),
            Math.Round(players.Max(player => player.Buzz), 2),
            players);
    }

    private static PlayerListItemDto ToListItem(Player player)
    {
        return new PlayerListItemDto(
            player.Id,
            player.Name,
            player.Team,
            player.Position,
            player.Aliases
                .OrderBy(alias => alias.Alias)
                .Select(alias => alias.Alias)
                .ToArray());
    }

    private static PlayerMetricsDto ToMetrics(Player player, PlayerSnapshot? snapshot)
    {
        var mentions = snapshot?.MentionCount ?? 0;
        var positive = snapshot?.PositiveCount ?? 0;
        var neutral = snapshot?.NeutralCount ?? 0;
        var negative = snapshot?.NegativeCount ?? 0;
        var denominator = Math.Max(1, mentions);

        return new PlayerMetricsDto(
            player.Id,
            player.Name,
            player.Team,
            player.Position,
            player.Aliases.OrderBy(alias => alias.Alias).Select(alias => alias.Alias).ToArray(),
            mentions,
            snapshot?.BuzzScore ?? 1,
            snapshot?.SentimentScore ?? 0,
            Math.Round(positive * 100.0 / denominator, 1),
            Math.Round(neutral * 100.0 / denominator, 1),
            Math.Round(negative * 100.0 / denominator, 1),
            snapshot?.AddCount ?? 0,
            snapshot?.DropCount ?? 0,
            snapshot?.StartCount ?? 0,
            snapshot?.SitCount ?? 0,
            snapshot?.BuyCount ?? 0,
            snapshot?.SellCount ?? 0,
            snapshot?.HoldCount ?? 0,
            snapshot?.InjuryCount ?? 0,
            snapshot?.HypeCount ?? 0,
            snapshot?.PanicCount ?? 0);
    }
}
