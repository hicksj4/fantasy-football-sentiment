using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Dtos;
using FantasySentiment.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Services;

public sealed class SnapshotQueryService(
    FantasySentimentDbContext db,
    IOptions<DashboardHighlightOptions> highlightOptions)
{
    private readonly DashboardHighlightOptions _highlightOptions = highlightOptions.Value;

    public async Task<IReadOnlyList<AnalysisSnapshotDto>> GetSnapshotsAsync(CancellationToken cancellationToken)
    {
        var snapshots = await db.AnalysisSnapshots
            .AsNoTracking()
            .OrderByDescending(snapshot => snapshot.Season)
            .ThenByDescending(snapshot => snapshot.Week)
            .ToListAsync(cancellationToken);

        return snapshots.Select(ToSnapshotDto).ToList();
    }

    public async Task<AnalysisSnapshotDto?> GetSnapshotAsync(string key, CancellationToken cancellationToken)
    {
        var snapshot = await ResolveSnapshotAsync(key, cancellationToken);
        return snapshot is null ? null : ToSnapshotDto(snapshot);
    }

    public async Task<IReadOnlyList<DashboardPlayerDto>> GetPlayersAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var snapshot = await ResolveSnapshotAsync(key, cancellationToken);
        if (snapshot is null)
        {
            return [];
        }

        return await db.SnapshotPlayerAggregates
            .AsNoTracking()
            .Include(aggregate => aggregate.Player)
            .Where(aggregate => aggregate.SnapshotId == snapshot.Id)
            .OrderByDescending(aggregate => aggregate.ImpactScore)
            .ThenByDescending(aggregate => aggregate.MentionCount)
            .Select(aggregate => ToDashboardPlayer(aggregate))
            .ToListAsync(cancellationToken);
    }

    public async Task<PlayerMetricsDto?> GetPlayerAsync(
        string key,
        int playerId,
        CancellationToken cancellationToken)
    {
        var snapshot = await ResolveSnapshotAsync(key, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var aggregate = await db.SnapshotPlayerAggregates
            .AsNoTracking()
            .Include(item => item.Player)
            .ThenInclude(player => player.Aliases)
            .FirstOrDefaultAsync(
                item => item.SnapshotId == snapshot.Id && item.PlayerId == playerId,
                cancellationToken);

        return aggregate is null ? null : ToPlayerMetrics(aggregate);
    }

    public async Task<DashboardHighlightsDto?> GetHighlightsAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var snapshot = await ResolveSnapshotAsync(key, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var candidates = await db.SnapshotPlayerAggregates
            .AsNoTracking()
            .Include(aggregate => aggregate.Player)
            .Where(aggregate => aggregate.SnapshotId == snapshot.Id)
            .Where(IsHighlightEligibleExpression())
            .ToListAsync(cancellationToken);

        var positive = candidates
            .Where(aggregate => aggregate.PositiveImpact > 0)
            .OrderByDescending(aggregate => aggregate.PositiveImpact)
            .ThenByDescending(aggregate => aggregate.MentionCount)
            .FirstOrDefault();

        var negative = candidates
            .Where(aggregate => aggregate.NegativeImpact < 0)
            .OrderBy(aggregate => aggregate.NegativeImpact)
            .ThenByDescending(aggregate => aggregate.MentionCount)
            .FirstOrDefault();

        return new DashboardHighlightsDto(
            positive is null ? null : await ToHighlightAsync(positive, 1, cancellationToken),
            negative is null ? null : await ToHighlightAsync(negative, -1, cancellationToken),
            new DashboardHighlightEligibilityDto(
                _highlightOptions.MinimumMentions,
                _highlightOptions.MinimumSampleStrength,
                _highlightOptions.ExcludeLowConfidence,
                _highlightOptions.MinimumAbsoluteSentimentChange));
    }

    public async Task<IReadOnlyList<TeamSummaryDto>> GetTeamSummariesAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var rows = await GetPlayersAsync(key, cancellationToken);
        return rows
            .GroupBy(row => row.Team)
            .Select(group =>
            {
                var players = group.ToList();
                var mentions = players.Sum(player => player.Mentions);
                return new TeamSummaryDto(
                    group.Key,
                    mentions,
                    mentions == 0 ? 0 : Math.Round(players.Sum(player => player.CommunitySentiment * player.Mentions) / mentions, 3),
                    players.Sum(player => player.DiscussionShare),
                    players.OrderByDescending(player => player.Mentions).FirstOrDefault()?.Name,
                    players.OrderByDescending(player => player.PositiveImpact).FirstOrDefault()?.Name,
                    players.OrderByDescending(player => player.ImpactScore).FirstOrDefault()?.Name);
            })
            .OrderByDescending(team => team.Mentions)
            .ToList();
    }

    public async Task<TeamDetailDto?> GetTeamDetailAsync(
        string key,
        string team,
        CancellationToken cancellationToken)
    {
        var players = (await GetPlayersAsync(key, cancellationToken))
            .Where(player => player.Team.Equals(team, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(player => player.Mentions)
            .ToList();

        if (players.Count == 0)
        {
            return null;
        }

        var mentions = players.Sum(player => player.Mentions);
        return new TeamDetailDto(
            players[0].Team,
            mentions,
            mentions == 0 ? 0 : Math.Round(players.Sum(player => player.CommunitySentiment * player.Mentions) / mentions, 3),
            Math.Round(players.Sum(player => player.DiscussionShare), 4),
            players);
    }

    private async Task<AnalysisSnapshot?> ResolveSnapshotAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var query = db.AnalysisSnapshots.AsNoTracking();

        if (key.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            return await query
                .Where(snapshot => snapshot.Status == AnalysisSnapshotStatuses.Processed)
                .OrderByDescending(snapshot => snapshot.Season)
                .ThenByDescending(snapshot => snapshot.Week)
                .ThenByDescending(snapshot => snapshot.ProcessedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await query.FirstOrDefaultAsync(
            snapshot => snapshot.Key == key,
            cancellationToken);
    }

    private System.Linq.Expressions.Expression<Func<SnapshotPlayerAggregate, bool>> IsHighlightEligibleExpression()
    {
        return aggregate =>
            aggregate.MentionCount >= _highlightOptions.MinimumMentions
            && aggregate.SampleStrengthScore >= _highlightOptions.MinimumSampleStrength
            && (!_highlightOptions.ExcludeLowConfidence || aggregate.Confidence != "Low");
    }

    private async Task<DashboardHighlightPlayerDto> ToHighlightAsync(
        SnapshotPlayerAggregate aggregate,
        int direction,
        CancellationToken cancellationToken)
    {
        var influence = await GetTopInfluenceAsync(
            aggregate.SnapshotId,
            aggregate.PlayerId,
            direction,
            cancellationToken);

        var impact = direction >= 0 ? aggregate.PositiveImpact : aggregate.NegativeImpact;
        return new DashboardHighlightPlayerDto(
            aggregate.PlayerId,
            aggregate.Player.Name,
            aggregate.Player.Team,
            aggregate.Player.Position,
            null,
            aggregate.MentionCount,
            aggregate.SentimentScore,
            aggregate.CommunitySentimentScore,
            0,
            impact,
            aggregate.TotalVoteWeight,
            aggregate.AverageVoteWeight,
            aggregate.SampleStrengthScore,
            aggregate.Confidence,
            influence);
    }

    private async Task<DashboardHighlightInfluenceDto?> GetTopInfluenceAsync(
        int snapshotId,
        int playerId,
        int direction,
        CancellationToken cancellationToken)
    {
        var mentions = await db.PlayerMentions
            .AsNoTracking()
            .Where(mention => mention.SnapshotId == snapshotId && mention.PlayerId == playerId)
            .Select(mention => new
            {
                mention.FinalSentimentScore,
                mention.VoteWeight,
                mention.RedditScore,
                mention.CreatedAt,
                CommentBody = mention.Comment.Body,
                PostTitle = mention.Comment.Post.Title
            })
            .ToListAsync(cancellationToken);

        var totalVoteWeight = mentions.Sum(mention => mention.VoteWeight);
        if (totalVoteWeight <= 0)
        {
            return null;
        }

        var influences = mentions
            .Select(mention => new
            {
                mention.CommentBody,
                mention.RedditScore,
                Sentiment = mention.FinalSentimentScore,
                mention.VoteWeight,
                SentimentContribution = mention.FinalSentimentScore * mention.VoteWeight / totalVoteWeight,
                mention.PostTitle,
                mention.CreatedAt
            })
            .ToList();

        var selected = direction >= 0
            ? influences
                .Where(influence => influence.SentimentContribution > 0)
                .OrderByDescending(influence => influence.SentimentContribution)
                .FirstOrDefault()
            : influences
                .Where(influence => influence.SentimentContribution < 0)
                .OrderBy(influence => influence.SentimentContribution)
                .FirstOrDefault();

        return selected is null
            ? null
            : new DashboardHighlightInfluenceDto(
                CreateExcerpt(selected.CommentBody, _highlightOptions.CommentExcerptLength),
                selected.RedditScore,
                Math.Round(selected.Sentiment, 3),
                Math.Round(selected.VoteWeight, 3),
                Math.Round(selected.SentimentContribution, 3),
                selected.PostTitle,
                selected.CreatedAt);
    }

    private static AnalysisSnapshotDto ToSnapshotDto(AnalysisSnapshot snapshot)
    {
        return new AnalysisSnapshotDto(
            snapshot.Id,
            snapshot.Key,
            snapshot.Name,
            snapshot.Season,
            snapshot.Week,
            snapshot.CreatedAt,
            snapshot.ProcessedAt,
            snapshot.Status,
            snapshot.TotalPosts,
            snapshot.TotalComments,
            snapshot.TotalPlayerMentions,
            snapshot.UniquePlayersMentioned,
            snapshot.ProcessingDurationMs,
            snapshot.CommentsPerSecond);
    }

    private static DashboardPlayerDto ToDashboardPlayer(SnapshotPlayerAggregate aggregate)
    {
        return new DashboardPlayerDto(
            aggregate.PlayerId,
            aggregate.Player.Name,
            aggregate.Player.Team,
            aggregate.Player.Position,
            aggregate.MentionCount,
            aggregate.DiscussionShare,
            aggregate.SentimentScore,
            aggregate.RawSentimentScore,
            aggregate.CommunitySentimentScore,
            0,
            aggregate.TotalRedditScore,
            aggregate.AverageVoteWeight,
            aggregate.SampleStrengthScore,
            aggregate.Confidence,
            aggregate.AddCount,
            aggregate.DropCount,
            aggregate.StartCount,
            aggregate.SitCount,
            aggregate.BuyCount,
            aggregate.SellCount,
            aggregate.HypeCount,
            aggregate.PanicCount,
            aggregate.DiscussionShare,
            aggregate.PositiveImpact,
            aggregate.NegativeImpact,
            aggregate.NetImpact,
            aggregate.ImpactScore);
    }

    private static PlayerMetricsDto ToPlayerMetrics(SnapshotPlayerAggregate aggregate)
    {
        var denominator = Math.Max(1, aggregate.MentionCount);
        return new PlayerMetricsDto(
            aggregate.PlayerId,
            aggregate.Player.Name,
            aggregate.Player.Team,
            aggregate.Player.Position,
            aggregate.Player.Aliases.OrderBy(alias => alias.Alias).Select(alias => alias.Alias).ToArray(),
            aggregate.MentionCount,
            aggregate.DiscussionShare,
            aggregate.SentimentScore,
            aggregate.RawSentimentScore,
            aggregate.CommunitySentimentScore,
            aggregate.TotalRedditScore,
            aggregate.AverageVoteWeight,
            aggregate.SampleStrengthScore,
            aggregate.Confidence,
            Math.Round(aggregate.PositiveCount * 100.0 / denominator, 1),
            Math.Round(aggregate.NeutralCount * 100.0 / denominator, 1),
            Math.Round(aggregate.NegativeCount * 100.0 / denominator, 1),
            aggregate.AddCount,
            aggregate.DropCount,
            aggregate.StartCount,
            aggregate.SitCount,
            aggregate.BuyCount,
            aggregate.SellCount,
            aggregate.HoldCount,
            aggregate.InjuryCount,
            aggregate.HypeCount,
            aggregate.PanicCount);
    }

    private static string CreateExcerpt(string value, int maxLength)
    {
        maxLength = Math.Clamp(maxLength, 80, 240);
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        var trimmed = normalized[..maxLength];
        var lastSpace = trimmed.LastIndexOf(' ');
        if (lastSpace >= maxLength * 0.65)
        {
            trimmed = trimmed[..lastSpace];
        }

        return $"{trimmed.TrimEnd()}...";
    }
}
