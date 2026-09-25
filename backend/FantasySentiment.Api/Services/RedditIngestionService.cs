using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using FantasySentiment.Api.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Services;

public sealed class RedditIngestionService(
    FantasySentimentDbContext db,
    IRedditDataProvider redditDataProvider,
    RedditPostAnalysisDecider analysisDecider,
    PlayerMentionDetector mentionDetector,
    ISentimentAnalyzer sentimentAnalyzer,
    TargetContextBuilder targetContextBuilder,
    ThreadContextBuilder threadContextBuilder,
    FantasyIntentClassifier intentClassifier,
    VoteWeightCalculator voteWeightCalculator,
    SnapshotAggregationService snapshotAggregationService,
    IOptions<RedditOptions> redditOptions,
    IOptions<RedditAnalysisOptions> analysisOptions,
    ILogger<RedditIngestionService> logger)
{
    private readonly RedditOptions _redditOptions = redditOptions.Value;
    private readonly RedditAnalysisOptions _analysisOptions = analysisOptions.Value;

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var discovered = await DiscoverPostsAsync(cancellationToken);
        await AnalyzeReadyPostsAsync(cancellationToken);
        return discovered;
    }

    public async Task<int> DiscoverPostsAsync(CancellationToken cancellationToken)
    {
        var since = DateTimeOffset.UtcNow.AddMinutes(-Math.Max(15, _redditOptions.FetchLookbackMinutes));
        var now = DateTimeOffset.UtcNow;
        var discovered = 0;

        foreach (var subreddit in _redditOptions.Subreddits.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            logger.LogInformation("Reddit discovery started for r/{Subreddit}.", subreddit);
            var posts = await redditDataProvider.DiscoverRecentPostsAsync(subreddit, since, cancellationToken);
            logger.LogInformation("Discovered {PostCount} candidate posts from r/{Subreddit}.", posts.Count, subreddit);

            foreach (var postData in posts)
            {
                var post = await UpsertPostMetadataAsync(postData, now, cancellationToken);
                var decision = analysisDecider.Decide(post, now);
                post.PostType = decision.PostType;
                post.AnalysisStatus = decision.Status;
                post.AnalysisStatusReason = Truncate(decision.Reason, 500);

                logger.LogInformation(
                    "Post discovered: {PostId}. Comment count: {CommentCount}. Score: {Score}. Status: {Status}. Reason: {Reason}.",
                    post.RedditId,
                    post.CommentCount,
                    post.Score,
                    post.AnalysisStatus,
                    post.AnalysisStatusReason);
                discovered++;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return discovered;
    }

    public async Task<RedditAnalysisBatchResult> AnalyzeReadyPostsAsync(CancellationToken cancellationToken)
    {
        var readyPosts = await db.RedditPosts
            .Where(post => post.AnalysisStatus == RedditAnalysisStatuses.ReadyForAnalysis)
            .OrderBy(post => post.LastAnalyzedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(post => post.CommentCount)
            .Take(Math.Max(1, _analysisOptions.MaxPostsPerAnalysisBatch))
            .ToListAsync(cancellationToken);

        var result = new RedditAnalysisBatchResult();
        if (readyPosts.Count == 0)
        {
            logger.LogInformation("Reddit batch analysis skipped. No posts are ready for analysis.");
            return result;
        }

        var players = await db.Players
            .Include(player => player.Aliases)
            .Where(player => player.IsActive)
            .ToListAsync(cancellationToken);

        var batchStarted = DateTimeOffset.UtcNow;
        foreach (var post in readyPosts)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            post.AnalysisStatus = RedditAnalysisStatuses.Analyzing;
            post.AnalysisStatusReason = "analysis started";
            await db.SaveChangesAsync(cancellationToken);

            var comments = await redditDataProvider.FetchCommentsAsync(
                post.Subreddit,
                post.RedditId,
                post.CreatedAt,
                cancellationToken);

            var commentLookup = comments.ToDictionary(comment => comment.RedditId, StringComparer.OrdinalIgnoreCase);
            var insertedComments = 0;
            var changedComments = 0;
            var scoreOnlyUpdates = 0;
            var detectedMentions = 0;

            foreach (var commentData in comments)
            {
                if (IsUnavailableComment(commentData.Body))
                {
                    continue;
                }

                var upsert = await UpsertCommentAsync(post, commentData, cancellationToken);
                switch (upsert.Action)
                {
                    case CommentUpsertAction.New:
                        insertedComments++;
                        break;
                    case CommentUpsertAction.TextChanged:
                        changedComments++;
                        break;
                    case CommentUpsertAction.ScoreOnlyUpdated:
                        scoreOnlyUpdates++;
                        break;
                }

                if (upsert.ShouldAnalyze)
                {
                    detectedMentions += await AddMentionsForCommentAsync(
                        post,
                        upsert.Comment,
                        commentData,
                        commentLookup,
                        players,
                        cancellationToken);
                }
            }

            post.LastAnalyzedAt = DateTimeOffset.UtcNow;
            post.LastAnalyzedCommentCount = Math.Max(post.LastAnalyzedCommentCount, post.CommentCount);
            post.AnalysisStatus = RedditAnalysisStatuses.Analyzed;
            post.AnalysisStatusReason =
                $"analyzed {insertedComments} new comments, {changedComments} changed comments, {scoreOnlyUpdates} score-only updates";
            stopwatch.Stop();

            logger.LogInformation(
                "Batch analysis: Post {PostId}. New comments: {NewComments}. Changed comments: {ChangedComments}. Score updates: {ScoreUpdates}. Player mentions detected: {MentionCount}. Elapsed: {ElapsedMs}ms.",
                post.RedditId,
                insertedComments,
                changedComments,
                scoreOnlyUpdates,
                detectedMentions,
                stopwatch.ElapsedMilliseconds);

            result = result with
            {
                PostsAnalyzed = result.PostsAnalyzed + 1,
                NewComments = result.NewComments + insertedComments,
                ChangedComments = result.ChangedComments + changedComments,
                ScoreOnlyUpdates = result.ScoreOnlyUpdates + scoreOnlyUpdates,
                PlayerMentions = result.PlayerMentions + detectedMentions
            };

            await db.SaveChangesAsync(cancellationToken);
        }

        if (result.PostsAnalyzed > 0)
        {
            await snapshotAggregationService.GenerateSnapshotsAsync(batchStarted, cancellationToken);
        }

        return result;
    }

    private async Task<RedditPost> UpsertPostMetadataAsync(
        RedditPostData postData,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var post = await db.RedditPosts
            .FirstOrDefaultAsync(existing => existing.RedditId == postData.RedditId, cancellationToken);

        if (post is not null)
        {
            post.PreviousCommentCount = post.CommentCount;
            post.LastObservedCommentCountAt = post.LastObservedAt == default ? null : post.LastObservedAt;
            post.CommentCount = postData.CommentCount;
            post.Score = postData.Score;
            post.Title = postData.Title;
            post.Url = postData.Url;
            post.LastObservedAt = observedAt;
            return post;
        }

        post = new RedditPost
        {
            RedditId = postData.RedditId,
            Subreddit = postData.Subreddit,
            Title = postData.Title,
            CreatedAt = postData.CreatedAt,
            Url = postData.Url,
            Score = postData.Score,
            CommentCount = postData.CommentCount,
            PreviousCommentCount = 0,
            LastObservedAt = observedAt,
            AnalysisStatus = RedditAnalysisStatuses.Discovered,
            PostType = RedditPostTypes.Normal
        };

        db.RedditPosts.Add(post);
        await db.SaveChangesAsync(cancellationToken);
        return post;
    }

    private async Task<CommentUpsertResult> UpsertCommentAsync(
        RedditPost post,
        RedditCommentData commentData,
        CancellationToken cancellationToken)
    {
        var body = Truncate(commentData.Body, 4000) ?? string.Empty;
        var parentRedditId = NormalizeRedditThingId(commentData.ParentRedditId);
        var existingComment = await db.RedditComments
            .Include(comment => comment.PlayerMentions)
            .FirstOrDefaultAsync(comment => comment.RedditId == commentData.RedditId, cancellationToken);

        if (existingComment is null)
        {
            var comment = new RedditComment
            {
                RedditId = commentData.RedditId,
                PostId = post.Id,
                ParentRedditId = parentRedditId,
                Body = body,
                CreatedAt = commentData.CreatedAt,
                Score = commentData.Score
            };

            db.RedditComments.Add(comment);
            await db.SaveChangesAsync(cancellationToken);
            return new CommentUpsertResult(comment, CommentUpsertAction.New);
        }

        var textChanged = existingComment.Body != body;
        existingComment.PostId = post.Id;
        existingComment.ParentRedditId = parentRedditId;
        existingComment.CreatedAt = commentData.CreatedAt;

        if (textChanged)
        {
            existingComment.Body = body;
            existingComment.Score = commentData.Score;
            db.PlayerMentions.RemoveRange(existingComment.PlayerMentions);
            await db.SaveChangesAsync(cancellationToken);
            return new CommentUpsertResult(existingComment, CommentUpsertAction.TextChanged);
        }

        if (existingComment.Score != commentData.Score)
        {
            await RefreshExistingCommentScoreAsync(existingComment, commentData.Score, cancellationToken);
            return new CommentUpsertResult(existingComment, CommentUpsertAction.ScoreOnlyUpdated);
        }

        return new CommentUpsertResult(existingComment, CommentUpsertAction.Unchanged);
    }

    private async Task<int> AddMentionsForCommentAsync(
        RedditPost post,
        RedditComment comment,
        RedditCommentData commentData,
        IReadOnlyDictionary<string, RedditCommentData> commentLookup,
        IReadOnlyList<Player> players,
        CancellationToken cancellationToken)
    {
        var matches = mentionDetector.DetectMentions(comment.Body, players);
        var targets = matches
            .Select(match => ResolvedCommentTarget.FromMatch(
                match,
                TargetAttributionMethods.Explicit,
                1.0))
            .ToList();
        var voteWeight = voteWeightCalculator.Calculate(comment.Score);
        var added = 0;
        var parentBody = await FindParentBodyAsync(commentData, commentLookup, cancellationToken);

        foreach (var target in targets)
        {
            var match = target.ToMatch();
            var alreadyExists = await db.PlayerMentions.AnyAsync(
                mention => mention.CommentId == comment.Id && mention.PlayerId == match.PlayerId,
                cancellationToken);

            if (alreadyExists)
            {
                continue;
            }

            var resolvedContext = targetContextBuilder.Build(
                post.Title,
                parentBody,
                comment.Body,
                target,
                targets);
            var analysisContext = threadContextBuilder.Build(resolvedContext);
            var sentiment = await sentimentAnalyzer.AnalyzeAsync(
                new TargetedSentimentRequest(comment.Body, analysisContext, match),
                cancellationToken);

            db.PlayerMentions.Add(new PlayerMention
            {
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
                Intent = intentClassifier.Classify(comment.Body, match),
                RedditScore = comment.Score,
                VoteWeight = voteWeight,
                WeightedSentimentContribution = Math.Round(sentiment.FinalSentimentScore * voteWeight, 3)
            });
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    private async Task<string?> FindParentBodyAsync(
        RedditCommentData commentData,
        IReadOnlyDictionary<string, RedditCommentData> commentLookup,
        CancellationToken cancellationToken)
    {
        var parentId = NormalizeRedditThingId(commentData.ParentRedditId);
        if (string.IsNullOrWhiteSpace(parentId))
        {
            return null;
        }

        if (commentLookup.TryGetValue(parentId, out var parentData)
            && !IsUnavailableComment(parentData.Body))
        {
            return parentData.Body;
        }

        return await db.RedditComments
            .Where(comment => comment.RedditId == parentId)
            .Select(comment => comment.Body)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task RefreshExistingCommentScoreAsync(
        RedditComment existingComment,
        int latestScore,
        CancellationToken cancellationToken)
    {
        existingComment.Score = latestScore;
        var voteWeight = voteWeightCalculator.Calculate(latestScore);
        var mentions = await db.PlayerMentions
            .Where(mention => mention.CommentId == existingComment.Id)
            .ToListAsync(cancellationToken);

        foreach (var mention in mentions)
        {
            mention.RedditScore = latestScore;
            mention.VoteWeight = voteWeight;
            mention.WeightedSentimentContribution = Math.Round(mention.FinalSentimentScore * voteWeight, 3);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsUnavailableComment(string? body)
    {
        return string.IsNullOrWhiteSpace(body)
               || body is "[deleted]" or "[removed]";
    }

    private static string? NormalizeRedditThingId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.StartsWith("t1_", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("t3_", StringComparison.OrdinalIgnoreCase)
            ? value[3..]
            : value;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        return string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
}

public sealed record RedditAnalysisBatchResult(
    int PostsAnalyzed = 0,
    int NewComments = 0,
    int ChangedComments = 0,
    int ScoreOnlyUpdates = 0,
    int PlayerMentions = 0);

internal sealed record CommentUpsertResult(
    RedditComment Comment,
    CommentUpsertAction Action)
{
    public bool ShouldAnalyze => Action is CommentUpsertAction.New or CommentUpsertAction.TextChanged;
}

internal enum CommentUpsertAction
{
    New,
    TextChanged,
    ScoreOnlyUpdated,
    Unchanged
}
