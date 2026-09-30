using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Dtos;
using FantasySentiment.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Services;

public sealed class SnapshotQueryService(
    FantasySentimentDbContext db,
    IOptions<DashboardHighlightOptions> highlightOptions,
    IOptions<ScoreExplanationOptions>? scoreExplanationOptions = null,
    IOptions<SnapshotProcessingOptions>? snapshotProcessingOptions = null)
{
    private readonly DashboardHighlightOptions _highlightOptions = highlightOptions.Value;
    private readonly ScoreExplanationOptions _scoreExplanationOptions = scoreExplanationOptions?.Value ?? new ScoreExplanationOptions();
    private readonly SnapshotProcessingOptions _snapshotProcessingOptions = snapshotProcessingOptions?.Value ?? new SnapshotProcessingOptions();

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

        var aggregates = await db.SnapshotPlayerAggregates
            .AsNoTracking()
            .Include(aggregate => aggregate.Player)
            .Where(aggregate => aggregate.SnapshotId == snapshot.Id)
            .OrderByDescending(aggregate => aggregate.ImpactScore)
            .ThenByDescending(aggregate => aggregate.MentionCount)
            .ToListAsync(cancellationToken);
        var ranks = BuildDiscussionImpactRanks(aggregates);

        return aggregates
            .Select(aggregate => ToDashboardPlayer(aggregate, ranks[aggregate.PlayerId]))
            .ToList();
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

        if (aggregate is null)
        {
            return null;
        }

        var aggregates = await db.SnapshotPlayerAggregates
            .AsNoTracking()
            .Where(item => item.SnapshotId == snapshot.Id)
            .ToListAsync(cancellationToken);
        var ranks = BuildDiscussionImpactRanks(aggregates);
        var rank = ranks.TryGetValue(aggregate.PlayerId, out var foundRank)
            ? foundRank
            : new DiscussionImpactRank(0, 0);

        return await ToPlayerMetricsAsync(aggregate, rank, cancellationToken);
    }

    public async Task<PlayerMentionAnalysisPageDto?> GetPlayerMentionsAsync(
        string key,
        int playerId,
        string? disposition,
        string? sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var snapshot = await ResolveSnapshotAsync(key, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var mentions = await db.PlayerMentions
            .AsNoTracking()
            .Where(mention =>
                mention.SnapshotId == snapshot.Id
                && mention.PlayerId == playerId)
            .Select(mention => new MentionAnalysisSource(
                mention.Id,
                mention.CommentId,
                mention.Comment.Body,
                mention.Comment.ParentRedditId == null
                    ? null
                    : db.RedditComments
                        .Where(parent =>
                            parent.PostId == mention.Comment.PostId
                            && parent.RedditId == mention.Comment.ParentRedditId)
                        .Select(parent => parent.Body)
                        .FirstOrDefault(),
                mention.Comment.Post.Title,
                mention.Comment.Post.Subreddit,
                mention.RedditScore,
                mention.AnalysisDisposition,
                mention.AnalysisDispositionReason,
                mention.SemanticConfidence,
                mention.TargetConfidence,
                mention.TargetAttributionMethod,
                mention.RawSentimentScore,
                mention.FinalSentimentScore,
                mention.SentimentCategory,
                mention.FantasyActions,
                mention.DiscussionSignals,
                mention.SentimentDiagnostics,
                mention.VoteWeight,
                mention.CreatedAt))
            .ToListAsync(cancellationToken);

        var totalMentionCount = mentions.Count;
        var scoredMentionCount = mentions.Count(mention => SentimentScoringPolicy.IsScoredDisposition(mention.AnalysisDisposition));
        var mentionOnlyCount = totalMentionCount - scoredMentionCount;
        var filtered = FilterMentions(mentions, disposition).ToList();
        var ordered = SortMentions(filtered, sort).ToList();
        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToMentionAnalysisDto)
            .ToList();

        return new PlayerMentionAnalysisPageDto(
            playerId,
            snapshot.Key,
            page,
            pageSize,
            filtered.Count,
            totalMentionCount,
            scoredMentionCount,
            mentionOnlyCount,
            totalMentionCount == 0 ? 0 : Math.Round(scoredMentionCount / (double)totalMentionCount, 3),
            page * pageSize < filtered.Count,
            items);
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
            .Where(aggregate => aggregate.NetImpact > 0)
            .OrderByDescending(aggregate => aggregate.NetImpact)
            .ThenByDescending(aggregate => aggregate.MentionCount)
            .FirstOrDefault();

        var negative = candidates
            .Where(aggregate => aggregate.NetImpact < 0)
            .OrderBy(aggregate => aggregate.NetImpact)
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
                var scoredMentions = players.Sum(player => player.ScoredMentionCount);
                var mentionOnly = players.Sum(player => player.MentionOnlyCount);
                return new TeamSummaryDto(
                    group.Key,
                    mentions,
                    scoredMentions == 0
                        ? 0
                        : Math.Round(players.Sum(player => player.CommunitySentiment * player.ScoredMentionCount) / scoredMentions, 3),
                    players.Sum(player => player.DiscussionShare),
                    players.OrderByDescending(player => player.Mentions).FirstOrDefault()?.Name,
                    players.OrderByDescending(player => player.PositiveImpact).FirstOrDefault()?.Name,
                    players.OrderByDescending(player => player.ImpactScore).FirstOrDefault()?.Name,
                    TotalMentionCount: mentions,
                    ScoredMentionCount: scoredMentions,
                    MentionOnlyCount: mentionOnly,
                    SentimentCoverage: mentions == 0 ? 0 : Math.Round(scoredMentions / (double)mentions, 3),
                    HasScoredSentiment: scoredMentions > 0);
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
        var scoredMentions = players.Sum(player => player.ScoredMentionCount);
        var mentionOnly = players.Sum(player => player.MentionOnlyCount);
        return new TeamDetailDto(
            players[0].Team,
            mentions,
            scoredMentions == 0
                ? 0
                : Math.Round(players.Sum(player => player.CommunitySentiment * player.ScoredMentionCount) / scoredMentions, 3),
            Math.Round(players.Sum(player => player.DiscussionShare), 4),
            players,
            TotalMentionCount: mentions,
            ScoredMentionCount: scoredMentions,
            MentionOnlyCount: mentionOnly,
            SentimentCoverage: mentions == 0 ? 0 : Math.Round(scoredMentions / (double)mentions, 3),
            HasScoredSentiment: scoredMentions > 0);
    }

    private async Task<AnalysisSnapshot?> ResolveSnapshotAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var query = db.AnalysisSnapshots.AsNoTracking();

        if (key.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(_snapshotProcessingOptions.DefaultSnapshotKey))
            {
                var configuredDefault = await query.FirstOrDefaultAsync(
                    snapshot =>
                        snapshot.Key == _snapshotProcessingOptions.DefaultSnapshotKey
                        && snapshot.Status == AnalysisSnapshotStatuses.Processed,
                    cancellationToken);

                if (configuredDefault is not null)
                {
                    return configuredDefault;
                }
            }

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
            && aggregate.HasScoredSentiment
            && aggregate.ScoredMentionCount > 0
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

        var impact = aggregate.NetImpact;
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
            influence,
            TotalMentionCount: aggregate.MentionCount,
            ScoredMentionCount: aggregate.ScoredMentionCount,
            MentionOnlyCount: aggregate.MentionOnlyCount,
            SentimentCoverage: aggregate.SentimentCoverage,
            HasScoredSentiment: aggregate.HasScoredSentiment);
    }

    private async Task<DashboardHighlightInfluenceDto?> GetTopInfluenceAsync(
        int snapshotId,
        int playerId,
        int direction,
        CancellationToken cancellationToken)
    {
        var mentions = await db.PlayerMentions
            .AsNoTracking()
            .Where(mention =>
                mention.SnapshotId == snapshotId
                && mention.PlayerId == playerId
                && mention.AnalysisDisposition == AnalysisDispositions.Scored)
            .Select(mention => new
            {
                mention.FinalSentimentScore,
                mention.VoteWeight,
                mention.RedditScore,
                mention.SemanticConfidence,
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
                mention.SemanticConfidence,
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
                selected.CreatedAt,
                SemanticConfidence: Math.Round(selected.SemanticConfidence, 3));
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

    private static IReadOnlyDictionary<int, DiscussionImpactRank> BuildDiscussionImpactRanks(
        IReadOnlyList<SnapshotPlayerAggregate> aggregates)
    {
        if (aggregates.Count == 0)
        {
            return new Dictionary<int, DiscussionImpactRank>();
        }

        var ordered = aggregates
            .OrderByDescending(aggregate => aggregate.ImpactScore)
            .ThenByDescending(aggregate => aggregate.MentionCount)
            .ThenBy(aggregate => aggregate.PlayerId)
            .ToList();
        var result = new Dictionary<int, DiscussionImpactRank>();
        var previousImpact = double.NaN;
        var rank = 0;

        for (var index = 0; index < ordered.Count; index++)
        {
            var aggregate = ordered[index];
            if (index == 0 || Math.Abs(aggregate.ImpactScore - previousImpact) > 0.0001)
            {
                rank = index + 1;
                previousImpact = aggregate.ImpactScore;
            }

            var percentile = ordered.Count == 1
                ? 100
                : Math.Round((ordered.Count - rank) * 100.0 / (ordered.Count - 1), 1);
            result[aggregate.PlayerId] = new DiscussionImpactRank(rank, percentile);
        }

        return result;
    }

    private static DashboardPlayerDto ToDashboardPlayer(
        SnapshotPlayerAggregate aggregate,
        DiscussionImpactRank? impactRank = null)
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
            aggregate.ImpactScore,
            Math.Round(aggregate.CommunitySentimentScore - aggregate.SentimentScore, 3),
            aggregate.ImpactScore,
            impactRank?.Rank ?? 0,
            impactRank?.Percentile ?? 0,
            TotalMentionCount: aggregate.MentionCount,
            ScoredMentionCount: aggregate.ScoredMentionCount,
            MentionOnlyCount: aggregate.MentionOnlyCount,
            SentimentCoverage: aggregate.SentimentCoverage,
            HasScoredSentiment: aggregate.HasScoredSentiment);
    }

    private async Task<PlayerMetricsDto> ToPlayerMetricsAsync(
        SnapshotPlayerAggregate aggregate,
        DiscussionImpactRank impactRank,
        CancellationToken cancellationToken)
    {
        var denominator = Math.Max(1, aggregate.ScoredMentionCount);
        var comments = await GetScoreCommentsAsync(
            aggregate.SnapshotId,
            aggregate.PlayerId,
            cancellationToken);

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
            aggregate.PanicCount,
            Math.Round(aggregate.CommunitySentimentScore - aggregate.SentimentScore, 3),
            aggregate.ImpactScore,
            impactRank.Rank,
            impactRank.Percentile,
            aggregate.PositiveImpact,
            aggregate.NegativeImpact,
            aggregate.NetImpact,
            aggregate.PositiveCount,
            aggregate.NeutralCount,
            aggregate.NegativeCount,
            comments.MostInfluential,
            comments.MostEndorsed,
            comments.MostRejected,
            TotalMentionCount: aggregate.MentionCount,
            ScoredMentionCount: aggregate.ScoredMentionCount,
            MentionOnlyCount: aggregate.MentionOnlyCount,
            SentimentCoverage: aggregate.SentimentCoverage,
            HasScoredSentiment: aggregate.HasScoredSentiment);
    }

    private async Task<PlayerScoreCommentSet> GetScoreCommentsAsync(
        int snapshotId,
        int playerId,
        CancellationToken cancellationToken)
    {
        var mentions = await db.PlayerMentions
            .AsNoTracking()
            .Where(mention =>
                mention.SnapshotId == snapshotId
                && mention.PlayerId == playerId
                && mention.AnalysisDisposition == AnalysisDispositions.Scored)
            .Select(mention => new ScoreCommentSource(
                mention.Comment.Body,
                mention.Comment.Post.Title,
                mention.Comment.Post.Subreddit,
                mention.RedditScore,
                mention.VoteWeight,
                mention.FinalSentimentScore,
                mention.TargetAttributionMethod,
                mention.TargetConfidence,
                mention.SemanticConfidence,
                mention.AnalysisDisposition,
                mention.CreatedAt))
            .ToListAsync(cancellationToken);
        var totalVoteWeight = mentions.Sum(mention => mention.VoteWeight);
        if (totalVoteWeight <= 0 || mentions.Count == 0)
        {
            return new PlayerScoreCommentSet(null, null, null);
        }

        var comments = mentions
            .Select(mention => ToScoreComment(mention, totalVoteWeight))
            .ToList();
        var mostInfluential = comments
            .OrderByDescending(comment => Math.Abs(comment.CommunityContribution))
            .FirstOrDefault();
        var mostEndorsed = comments
            .OrderByDescending(comment => comment.RedditScore)
            .ThenByDescending(comment => Math.Abs(comment.CommunityContribution))
            .FirstOrDefault();
        var mostRejected = comments
            .Where(comment => comment.RedditScore <= _scoreExplanationOptions.MostRejectedMaximumRedditScore)
            .OrderBy(comment => comment.RedditScore)
            .ThenByDescending(comment => Math.Abs(comment.CommunityContribution))
            .FirstOrDefault();

        return new PlayerScoreCommentSet(mostInfluential, mostEndorsed, mostRejected);
    }

    private PlayerScoreCommentDto ToScoreComment(ScoreCommentSource mention, double totalVoteWeight)
    {
        var contribution = mention.FinalSentimentScore * mention.VoteWeight / totalVoteWeight;
        return new PlayerScoreCommentDto(
            CreateExcerpt(mention.CommentBody, _scoreExplanationOptions.CommentExcerptLength),
            mention.PostTitle,
            mention.Subreddit,
            mention.RedditScore,
            Math.Round(mention.VoteWeight, 3),
            Math.Round(mention.FinalSentimentScore, 3),
            Math.Round(contribution, 3),
            mention.TargetAttributionMethod,
            Math.Round(mention.TargetConfidence, 3),
            mention.CreatedAt,
            Math.Round(mention.SemanticConfidence, 3),
            mention.AnalysisDisposition);
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

    private static IEnumerable<MentionAnalysisSource> FilterMentions(
        IEnumerable<MentionAnalysisSource> mentions,
        string? disposition)
    {
        return NormalizeDispositionFilter(disposition) switch
        {
            "scored" => mentions.Where(mention => SentimentScoringPolicy.IsScoredDisposition(mention.AnalysisDisposition)),
            "mention-only" => mentions.Where(mention => !SentimentScoringPolicy.IsScoredDisposition(mention.AnalysisDisposition)),
            _ => mentions
        };
    }

    private static IEnumerable<MentionAnalysisSource> SortMentions(
        IEnumerable<MentionAnalysisSource> mentions,
        string? sort)
    {
        return NormalizeSort(sort) switch
        {
            "reddit-score" => mentions
                .OrderByDescending(mention => mention.RedditScore)
                .ThenByDescending(mention => Math.Abs(WeightedContributionValue(mention))),
            "confidence" => mentions
                .OrderByDescending(mention => mention.SemanticConfidence)
                .ThenByDescending(mention => mention.TargetConfidence)
                .ThenByDescending(mention => Math.Abs(WeightedContributionValue(mention))),
            _ => mentions
                .OrderByDescending(mention => SentimentScoringPolicy.IsScoredDisposition(mention.AnalysisDisposition))
                .ThenByDescending(mention => Math.Abs(WeightedContributionValue(mention)))
                .ThenByDescending(mention => mention.RedditScore)
                .ThenByDescending(mention => mention.SemanticConfidence)
        };
    }

    private static PlayerMentionAnalysisDto ToMentionAnalysisDto(MentionAnalysisSource mention)
    {
        var isScored = SentimentScoringPolicy.IsScoredDisposition(mention.AnalysisDisposition);
        return new PlayerMentionAnalysisDto(
            mention.Id,
            mention.CommentId,
            mention.Body,
            mention.ParentBody,
            mention.PostTitle,
            mention.Subreddit,
            mention.RedditScore,
            mention.AnalysisDisposition,
            mention.AnalysisDispositionReason,
            Math.Round(mention.SemanticConfidence, 3),
            Math.Round(mention.TargetConfidence, 3),
            mention.TargetAttributionMethod,
            Math.Round(mention.RawSentimentScore, 3),
            Math.Round(mention.FinalSentimentScore, 3),
            mention.FantasyOutlook,
            SplitLabels(mention.FantasyActions),
            SplitLabels(mention.DiscussionSignals),
            ExtractReasons(mention.AnalysisDispositionReason, mention.SentimentDiagnostics),
            Math.Round(mention.VoteWeight, 3),
            isScored ? Math.Round(mention.FinalSentimentScore * mention.VoteWeight, 3) : null,
            mention.CreatedAt);
    }

    private static double WeightedContributionValue(MentionAnalysisSource mention)
    {
        return SentimentScoringPolicy.IsScoredDisposition(mention.AnalysisDisposition)
            ? mention.FinalSentimentScore * mention.VoteWeight
            : 0;
    }

    private static string? NormalizeDispositionFilter(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "scored" => "scored",
            "mentiononly" or "mention-only" or "mention_only" => "mention-only",
            _ => null
        };
    }

    private static string NormalizeSort(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "reddit" or "reddit-score" or "score" => "reddit-score",
            "confidence" => "confidence",
            _ => "influence"
        };
    }

    private static string[] SplitLabels(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ExtractReasons(string dispositionReason, string? diagnostics)
    {
        var reasons = new List<string>();
        if (!string.IsNullOrWhiteSpace(dispositionReason)
            && !dispositionReason.Equals(AnalysisDispositionReasons.Scored, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add(dispositionReason);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics))
        {
            var rules = diagnostics
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(part => part.StartsWith("rules=", StringComparison.OrdinalIgnoreCase));
            if (rules is not null)
            {
                reasons.AddRange(rules["rules=".Length..]
                    .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Where(rule => !rule.Equals("none", StringComparison.OrdinalIgnoreCase))
                    .Take(6));
            }
        }

        return reasons
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record DiscussionImpactRank(int Rank, double Percentile);

    private sealed record PlayerScoreCommentSet(
        PlayerScoreCommentDto? MostInfluential,
        PlayerScoreCommentDto? MostEndorsed,
        PlayerScoreCommentDto? MostRejected);

    private sealed record ScoreCommentSource(
        string CommentBody,
        string PostTitle,
        string Subreddit,
        int RedditScore,
        double VoteWeight,
        double FinalSentimentScore,
        string TargetAttributionMethod,
        double TargetConfidence,
        double SemanticConfidence,
        string AnalysisDisposition,
        DateTimeOffset CreatedAt);

    private sealed record MentionAnalysisSource(
        long Id,
        int CommentId,
        string Body,
        string? ParentBody,
        string PostTitle,
        string Subreddit,
        int RedditScore,
        string AnalysisDisposition,
        string AnalysisDispositionReason,
        double SemanticConfidence,
        double TargetConfidence,
        string TargetAttributionMethod,
        double RawSentimentScore,
        double FinalSentimentScore,
        string FantasyOutlook,
        string FantasyActions,
        string DiscussionSignals,
        string? SentimentDiagnostics,
        double VoteWeight,
        DateTimeOffset CreatedAt);
}
