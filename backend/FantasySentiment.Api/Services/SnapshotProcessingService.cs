using System.Diagnostics;
using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FantasySentiment.Api.Services;

public sealed class SnapshotProcessingService(
    FantasySentimentDbContext db,
    ISnapshotSource snapshotSource,
    ICommentTargetResolver targetResolver,
    ISentimentAnalyzer sentimentAnalyzer,
    TargetContextBuilder targetContextBuilder,
    ThreadContextBuilder threadContextBuilder,
    FantasyIntentClassifier intentClassifier,
    VoteWeightCalculator voteWeightCalculator,
    ILogger<SnapshotProcessingService> logger)
{
    public async Task<AnalysisSnapshot> ProcessAsync(
        string key,
        bool skipIfProcessed,
        CancellationToken cancellationToken)
    {
        var existing = await db.AnalysisSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(snapshot => snapshot.Key == key, cancellationToken);

        if (skipIfProcessed && existing?.Status == AnalysisSnapshotStatuses.Processed)
        {
            var completeness = await CheckSnapshotCompletenessAsync(existing, cancellationToken);
            if (completeness.IsComplete)
            {
                logger.LogInformation(
                    "Snapshot {SnapshotKey} is already processed and data is complete. Processing skipped.",
                    key);
                return existing;
            }

            logger.LogWarning(
                "Snapshot {SnapshotKey} is marked processed, but stored data is incomplete ({Reason}). Reprocessing snapshot.",
                key,
                completeness.Reason);
        }

        var stopwatch = Stopwatch.StartNew();
        var source = await snapshotSource.LoadAsync(key, cancellationToken);

        logger.LogInformation(
            "Loading snapshot {SnapshotKey}. Posts loaded: {PostCount}. Comments loaded: {CommentCount}.",
            source.Key,
            source.Posts.Count,
            source.Posts.Sum(post => post.Comments.Count));

        var snapshot = await UpsertSnapshotShellAsync(source, cancellationToken);
        await ReplaceSnapshotSourceDataAsync(snapshot.Id, cancellationToken);

        snapshot.Status = AnalysisSnapshotStatuses.Processing;
        snapshot.ProcessedAt = null;
        snapshot.TotalPosts = source.Posts.Count;
        snapshot.TotalComments = source.Posts.Sum(post => post.Comments.Count);
        snapshot.TotalPlayerMentions = 0;
        snapshot.UniquePlayersMentioned = 0;
        snapshot.ProcessingDurationMs = 0;
        snapshot.CommentsPerSecond = 0;
        await db.SaveChangesAsync(cancellationToken);

        await ImportSourceDataAsync(snapshot, source, cancellationToken);

        logger.LogInformation("Analyzing comments for snapshot {SnapshotKey}.", source.Key);
        var mentionCount = await AnalyzeSnapshotCommentsAsync(snapshot.Id, cancellationToken);
        var aggregateCount = await AggregateSnapshotAsync(snapshot.Id, cancellationToken);

        stopwatch.Stop();
        snapshot.TotalPlayerMentions = mentionCount;
        snapshot.UniquePlayersMentioned = aggregateCount;
        snapshot.ProcessedAt = DateTimeOffset.UtcNow;
        snapshot.Status = AnalysisSnapshotStatuses.Processed;
        snapshot.ProcessingDurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1);
        snapshot.CommentsPerSecond = snapshot.TotalComments == 0
            ? 0
            : Math.Round(snapshot.TotalComments / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001), 1);
        await db.SaveChangesAsync(cancellationToken);

        var topPositive = await db.SnapshotPlayerAggregates
            .AsNoTracking()
            .Include(aggregate => aggregate.Player)
            .Where(aggregate => aggregate.SnapshotId == snapshot.Id)
            .OrderByDescending(aggregate => aggregate.PositiveImpact)
            .ThenByDescending(aggregate => aggregate.MentionCount)
            .FirstOrDefaultAsync(cancellationToken);

        var topNegative = await db.SnapshotPlayerAggregates
            .AsNoTracking()
            .Include(aggregate => aggregate.Player)
            .Where(aggregate => aggregate.SnapshotId == snapshot.Id)
            .OrderBy(aggregate => aggregate.NegativeImpact)
            .ThenByDescending(aggregate => aggregate.MentionCount)
            .FirstOrDefaultAsync(cancellationToken);

        logger.LogInformation(
            "Snapshot aggregation completed. Player mentions detected: {MentionCount}. Unique players: {UniquePlayers}. Top positive impact: {TopPositive}. Top negative impact: {TopNegative}. Elapsed: {ElapsedMs}ms. Throughput: {CommentsPerSecond} comments/sec.",
            mentionCount,
            aggregateCount,
            topPositive?.Player.Name ?? "none",
            topNegative?.Player.Name ?? "none",
            snapshot.ProcessingDurationMs,
            snapshot.CommentsPerSecond);

        return snapshot;
    }

    private async Task<SnapshotCompleteness> CheckSnapshotCompletenessAsync(
        AnalysisSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.TotalPosts <= 0 || snapshot.TotalComments <= 0 || snapshot.TotalPlayerMentions <= 0)
        {
            return SnapshotCompleteness.Incomplete("snapshot totals are empty");
        }

        var postCount = await db.RedditPosts
            .AsNoTracking()
            .CountAsync(post => post.SnapshotId == snapshot.Id, cancellationToken);
        if (postCount != snapshot.TotalPosts)
        {
            return SnapshotCompleteness.Incomplete(
                $"expected {snapshot.TotalPosts} posts, found {postCount}");
        }

        var commentCount = await db.RedditComments
            .AsNoTracking()
            .CountAsync(comment => comment.Post.SnapshotId == snapshot.Id, cancellationToken);
        if (commentCount != snapshot.TotalComments)
        {
            return SnapshotCompleteness.Incomplete(
                $"expected {snapshot.TotalComments} comments, found {commentCount}");
        }

        var mentionCount = await db.PlayerMentions
            .AsNoTracking()
            .CountAsync(mention => mention.SnapshotId == snapshot.Id, cancellationToken);
        if (mentionCount != snapshot.TotalPlayerMentions)
        {
            return SnapshotCompleteness.Incomplete(
                $"expected {snapshot.TotalPlayerMentions} player mentions, found {mentionCount}");
        }

        var aggregateCount = await db.SnapshotPlayerAggregates
            .AsNoTracking()
            .CountAsync(aggregate => aggregate.SnapshotId == snapshot.Id, cancellationToken);
        if (aggregateCount != snapshot.UniquePlayersMentioned)
        {
            return SnapshotCompleteness.Incomplete(
                $"expected {snapshot.UniquePlayersMentioned} player aggregates, found {aggregateCount}");
        }

        return SnapshotCompleteness.Complete;
    }

    private async Task<AnalysisSnapshot> UpsertSnapshotShellAsync(
        SnapshotSourceData source,
        CancellationToken cancellationToken)
    {
        var snapshot = await db.AnalysisSnapshots
            .FirstOrDefaultAsync(item => item.Key == source.Key, cancellationToken);

        if (snapshot is null)
        {
            snapshot = new AnalysisSnapshot
            {
                Key = source.Key,
                Name = source.Name,
                Season = source.Season,
                Week = source.Week,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = AnalysisSnapshotStatuses.Pending
            };
            db.AnalysisSnapshots.Add(snapshot);
        }
        else
        {
            snapshot.Name = source.Name;
            snapshot.Season = source.Season;
            snapshot.Week = source.Week;
        }

        await db.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    private async Task ReplaceSnapshotSourceDataAsync(int snapshotId, CancellationToken cancellationToken)
    {
        var aggregates = await db.SnapshotPlayerAggregates
            .Where(aggregate => aggregate.SnapshotId == snapshotId)
            .ToListAsync(cancellationToken);
        db.SnapshotPlayerAggregates.RemoveRange(aggregates);

        var mentions = await db.PlayerMentions
            .Where(mention => mention.SnapshotId == snapshotId)
            .ToListAsync(cancellationToken);
        db.PlayerMentions.RemoveRange(mentions);

        var posts = await db.RedditPosts
            .Include(post => post.Comments)
            .Where(post => post.SnapshotId == snapshotId)
            .ToListAsync(cancellationToken);
        db.RedditComments.RemoveRange(posts.SelectMany(post => post.Comments));
        db.RedditPosts.RemoveRange(posts);

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ImportSourceDataAsync(
        AnalysisSnapshot snapshot,
        SnapshotSourceData source,
        CancellationToken cancellationToken)
    {
        foreach (var sourcePost in source.Posts)
        {
            var post = new RedditPost
            {
                SnapshotId = snapshot.Id,
                RedditId = sourcePost.RedditId,
                Subreddit = sourcePost.Subreddit,
                Title = sourcePost.Title,
                CreatedAt = sourcePost.CreatedAt,
                Url = $"https://reddit.com/r/fantasyfootball/comments/{sourcePost.RedditId}",
                Score = sourcePost.Score,
                CommentCount = sourcePost.Comments.Count,
                PreviousCommentCount = sourcePost.Comments.Count,
                LastObservedAt = sourcePost.CreatedAt,
                LastObservedCommentCountAt = sourcePost.CreatedAt,
                LastAnalyzedAt = DateTimeOffset.UtcNow,
                LastAnalyzedCommentCount = sourcePost.Comments.Count,
                AnalysisStatus = RedditAnalysisStatuses.Analyzed,
                PostType = sourcePost.PostType,
                AnalysisStatusReason = "imported from snapshot fixture"
            };

            db.RedditPosts.Add(post);
            foreach (var sourceComment in sourcePost.Comments)
            {
                db.RedditComments.Add(new RedditComment
                {
                    RedditId = sourceComment.RedditId,
                    Post = post,
                    ParentRedditId = sourceComment.ParentRedditId,
                    Body = Truncate(sourceComment.Body, 4000) ?? string.Empty,
                    CreatedAt = sourceComment.CreatedAt,
                    Score = sourceComment.Score
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> AnalyzeSnapshotCommentsAsync(int snapshotId, CancellationToken cancellationToken)
    {
        var players = await db.Players
            .Include(player => player.Aliases)
            .Where(player => player.IsActive)
            .ToListAsync(cancellationToken);

        var comments = await db.RedditComments
            .Include(comment => comment.Post)
            .Where(comment => comment.Post.SnapshotId == snapshotId)
            .OrderBy(comment => comment.CreatedAt)
            .ToListAsync(cancellationToken);

        var commentsByRedditId = comments.ToDictionary(
            comment => comment.RedditId,
            comment => comment,
            StringComparer.OrdinalIgnoreCase);
        var resolutionByCommentId = new Dictionary<int, CommentTargetResolution>();
        var processingCommentIds = new HashSet<int>();
        var resolutionStats = new TargetResolutionStats(comments.Count);

        var detectedMentions = 0;
        foreach (var comment in comments)
        {
            var resolution = ResolveCommentTargets(
                comment,
                players,
                commentsByRedditId,
                resolutionByCommentId,
                processingCommentIds,
                resolutionStats);
            if (resolution.Targets.Count == 0)
            {
                continue;
            }

            var voteWeight = voteWeightCalculator.Calculate(comment.Score);
            var parentBody = !string.IsNullOrWhiteSpace(comment.ParentRedditId)
                             && commentsByRedditId.TryGetValue(comment.ParentRedditId, out var parent)
                ? parent.Body
                : null;

            foreach (var target in resolution.Targets)
            {
                var resolvedContext = targetContextBuilder.Build(
                    comment.Post.Title,
                    parentBody,
                    comment.Body,
                    target,
                    resolution.Targets);
                var match = resolvedContext.Target.ToMatch();
                var analysisContext = threadContextBuilder.Build(resolvedContext);
                var sentiment = await sentimentAnalyzer.AnalyzeAsync(
                    new TargetedSentimentRequest(
                        comment.Body,
                        analysisContext,
                        match,
                        resolvedContext.TargetText,
                        resolvedContext.Perspective,
                        target.AttributionMethod),
                    cancellationToken);

                db.PlayerMentions.Add(new PlayerMention
                {
                    SnapshotId = snapshotId,
                    PlayerId = match.PlayerId,
                    CommentId = comment.Id,
                    CreatedAt = comment.CreatedAt,
                    RawSentimentScore = sentiment.RawSentimentScore,
                    FinalSentimentScore = sentiment.FinalSentimentScore,
                    SentimentScore = sentiment.FinalSentimentScore,
                    ModelSentimentScore = sentiment.ModelScore,
                    RuleBasedSentimentScore = sentiment.RuleBasedScore,
                    FantasyAdjustment = sentiment.FantasyAdjustment,
                    SentimentCategory = sentiment.SentimentCategory,
                    SentimentAnalyzer = sentiment.AnalyzerName,
                    SentimentContext = Truncate(sentiment.Context, 1200) ?? string.Empty,
                    SentimentDiagnostics = Truncate(sentiment.Diagnostics, 1000),
                    MatchedAlias = Truncate(match.MatchedAlias, 160) ?? string.Empty,
                    TargetAttributionMethod = target.AttributionMethod,
                    TargetConfidence = target.Confidence,
                    TargetPerspective = resolvedContext.Perspective,
                    Intent = intentClassifier.Classify(comment.Body, resolvedContext.TargetText, match),
                    RedditScore = comment.Score,
                    VoteWeight = voteWeight,
                    WeightedSentimentContribution = Math.Round(sentiment.FinalSentimentScore * voteWeight, 3)
                });
                detectedMentions++;
            }
        }

        logger.LogInformation(
            "Target resolution summary. Comments: {TotalComments}. Explicit: {Explicit}. Parent inherited: {ParentInherited}. Ancestor inherited: {AncestorInherited}. Title inherited: {ThreadTitleInherited}. Ambiguous comments: {Ambiguous}. Unresolved comments: {Unresolved}.",
            resolutionStats.TotalComments,
            resolutionStats.ExplicitTargets,
            resolutionStats.ParentInheritedTargets,
            resolutionStats.AncestorInheritedTargets,
            resolutionStats.ThreadTitleInheritedTargets,
            resolutionStats.AmbiguousComments,
            resolutionStats.UnresolvedComments);

        await db.SaveChangesAsync(cancellationToken);
        return detectedMentions;
    }

    private CommentTargetResolution ResolveCommentTargets(
        RedditComment comment,
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<string, RedditComment> commentsByRedditId,
        Dictionary<int, CommentTargetResolution> resolutionByCommentId,
        HashSet<int> processingCommentIds,
        TargetResolutionStats stats)
    {
        if (resolutionByCommentId.TryGetValue(comment.Id, out var cached))
        {
            return cached;
        }

        if (!processingCommentIds.Add(comment.Id))
        {
            var cycleResolution = CommentTargetResolution.Unresolved("cycle detected");
            resolutionByCommentId[comment.Id] = cycleResolution;
            TrackResolution(comment, cycleResolution, stats);
            return cycleResolution;
        }

        IReadOnlyList<ResolvedCommentTarget> parentTargets = [];
        if (!string.IsNullOrWhiteSpace(comment.ParentRedditId)
            && commentsByRedditId.TryGetValue(comment.ParentRedditId, out var parentComment))
        {
            parentTargets = ResolveCommentTargets(
                parentComment,
                players,
                commentsByRedditId,
                resolutionByCommentId,
                processingCommentIds,
                stats).Targets;
        }

        var resolution = targetResolver.Resolve(new CommentTargetResolutionRequest(
            comment.RedditId,
            comment.Body,
            comment.Post.Title,
            players,
            parentTargets));

        resolutionByCommentId[comment.Id] = resolution;
        processingCommentIds.Remove(comment.Id);
        TrackResolution(comment, resolution, stats);
        return resolution;
    }

    private void TrackResolution(
        RedditComment comment,
        CommentTargetResolution resolution,
        TargetResolutionStats stats)
    {
        if (resolution.Targets.Count == 0)
        {
            if (resolution.UnresolvedReason?.Contains("ambiguous", StringComparison.OrdinalIgnoreCase) == true)
            {
                stats.AmbiguousComments++;
            }
            else
            {
                stats.UnresolvedComments++;
            }

            logger.LogDebug(
                "Skipped comment {CommentId}. Reason: {Reason}. Candidates: {Candidates}.",
                comment.RedditId,
                resolution.UnresolvedReason,
                string.Join(", ", resolution.CandidateNames));
            return;
        }

        foreach (var target in resolution.Targets)
        {
            stats.CountTarget(target.AttributionMethod);
            logger.LogDebug(
                "Resolved comment {CommentId}. Target: {Target}. Method: {Method}. Confidence: {Confidence}.",
                comment.RedditId,
                target.PlayerName,
                target.AttributionMethod,
                target.Confidence);
        }
    }

    private async Task<int> AggregateSnapshotAsync(int snapshotId, CancellationToken cancellationToken)
    {
        var mentions = await db.PlayerMentions
            .AsNoTracking()
            .Where(mention => mention.SnapshotId == snapshotId)
            .ToListAsync(cancellationToken);

        var totalMentions = Math.Max(1, mentions.Count);
        var grouped = mentions
            .GroupBy(mention => mention.PlayerId)
            .ToList();

        foreach (var group in grouped)
        {
            var playerMentions = group.ToList();
            var totalVoteWeight = playerMentions.Sum(mention => mention.VoteWeight);
            var sampleConfidence = SampleConfidenceCalculator.Calculate(playerMentions.Count, totalVoteWeight);
            var positiveImpact = playerMentions
                .Where(mention => mention.FinalSentimentScore > 0)
                .Sum(mention => mention.FinalSentimentScore * mention.VoteWeight);
            var negativeImpact = playerMentions
                .Where(mention => mention.FinalSentimentScore < 0)
                .Sum(mention => mention.FinalSentimentScore * mention.VoteWeight);

            db.SnapshotPlayerAggregates.Add(new SnapshotPlayerAggregate
            {
                SnapshotId = snapshotId,
                PlayerId = group.Key,
                MentionCount = playerMentions.Count,
                PositiveCount = playerMentions.Count(mention => mention.SentimentCategory == SentimentCategories.Positive),
                NegativeCount = playerMentions.Count(mention => mention.SentimentCategory == SentimentCategories.Negative),
                NeutralCount = playerMentions.Count(mention => mention.SentimentCategory == SentimentCategories.Neutral),
                RawSentimentScore = Math.Round(playerMentions.Average(mention => mention.RawSentimentScore), 3),
                SentimentScore = Math.Round(playerMentions.Average(mention => mention.FinalSentimentScore), 3),
                CommunitySentimentScore = totalVoteWeight == 0
                    ? 0
                    : Math.Round(playerMentions.Sum(mention => mention.WeightedSentimentContribution) / totalVoteWeight, 3),
                TotalRedditScore = playerMentions.Sum(mention => mention.RedditScore),
                TotalVoteWeight = Math.Round(totalVoteWeight, 3),
                AverageVoteWeight = Math.Round(playerMentions.Average(mention => mention.VoteWeight), 3),
                SampleStrengthScore = sampleConfidence.SampleStrengthScore,
                Confidence = sampleConfidence.Confidence,
                AddCount = CountIntent(playerMentions, FantasyIntents.Add),
                DropCount = CountIntent(playerMentions, FantasyIntents.Drop),
                StartCount = CountIntent(playerMentions, FantasyIntents.Start),
                SitCount = CountIntent(playerMentions, FantasyIntents.Sit),
                BuyCount = CountIntent(playerMentions, FantasyIntents.Buy),
                SellCount = CountIntent(playerMentions, FantasyIntents.Sell),
                HoldCount = CountIntent(playerMentions, FantasyIntents.Hold),
                InjuryCount = CountIntent(playerMentions, FantasyIntents.Injury),
                HypeCount = CountIntent(playerMentions, FantasyIntents.Hype),
                PanicCount = CountIntent(playerMentions, FantasyIntents.Panic),
                DiscussionShare = Math.Round(playerMentions.Count / (double)totalMentions, 4),
                PositiveImpact = Math.Round(positiveImpact, 3),
                NegativeImpact = Math.Round(negativeImpact, 3),
                NetImpact = Math.Round(positiveImpact + negativeImpact, 3),
                ImpactScore = Math.Round(Math.Abs(positiveImpact) + Math.Abs(negativeImpact), 3)
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return grouped.Count;
    }

    private static int CountIntent(IEnumerable<PlayerMention> mentions, string intent)
    {
        return mentions.Count(mention => mention.Intent == intent);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        return string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];
    }

    private sealed class TargetResolutionStats(int totalComments)
    {
        public int TotalComments { get; } = totalComments;
        public int ExplicitTargets { get; private set; }
        public int ParentInheritedTargets { get; private set; }
        public int AncestorInheritedTargets { get; private set; }
        public int ThreadTitleInheritedTargets { get; private set; }
        public int AmbiguousComments { get; set; }
        public int UnresolvedComments { get; set; }

        public void CountTarget(string attributionMethod)
        {
            switch (attributionMethod)
            {
                case TargetAttributionMethods.Explicit:
                    ExplicitTargets++;
                    break;
                case TargetAttributionMethods.ParentInherited:
                    ParentInheritedTargets++;
                    break;
                case TargetAttributionMethods.AncestorInherited:
                    AncestorInheritedTargets++;
                    break;
                case TargetAttributionMethods.ThreadTitleInherited:
                    ThreadTitleInheritedTargets++;
                    break;
            }
        }
    }

    private sealed record SnapshotCompleteness(bool IsComplete, string Reason)
    {
        public static SnapshotCompleteness Complete { get; } = new(true, "complete");

        public static SnapshotCompleteness Incomplete(string reason)
        {
            return new SnapshotCompleteness(false, reason);
        }
    }
}
