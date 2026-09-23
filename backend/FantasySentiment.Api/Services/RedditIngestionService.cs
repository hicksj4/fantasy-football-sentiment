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
    PlayerMentionDetector mentionDetector,
    ISentimentAnalyzer sentimentAnalyzer,
    MentionContextExtractor contextExtractor,
    FantasyIntentClassifier intentClassifier,
    VoteWeightCalculator voteWeightCalculator,
    IOptions<RedditOptions> options,
    ILogger<RedditIngestionService> logger)
{
    private readonly RedditOptions _options = options.Value;

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var since = DateTimeOffset.UtcNow.AddMinutes(-Math.Max(15, _options.FetchLookbackMinutes));
        var players = await db.Players
            .Include(player => player.Aliases)
            .Where(player => player.IsActive)
            .ToListAsync(cancellationToken);

        var insertedComments = 0;
        var detectedMentions = 0;

        foreach (var subreddit in _options.Subreddits.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            logger.LogInformation("Reddit ingestion started for r/{Subreddit}.", subreddit);
            var posts = await redditDataProvider.FetchRecentPostsAsync(subreddit, since, cancellationToken);
            logger.LogInformation("Fetched {PostCount} posts from r/{Subreddit}.", posts.Count, subreddit);

            foreach (var postData in posts)
            {
                var post = await UpsertPostAsync(postData, cancellationToken);
                foreach (var commentData in postData.Comments)
                {
                    if (string.IsNullOrWhiteSpace(commentData.Body)
                        || commentData.Body is "[deleted]" or "[removed]")
                    {
                        continue;
                    }

                    var existingComment = await db.RedditComments
                        .FirstOrDefaultAsync(comment => comment.RedditId == commentData.RedditId, cancellationToken);

                    if (existingComment is not null)
                    {
                        await RefreshExistingCommentScoreAsync(existingComment, commentData.Score, cancellationToken);
                        continue;
                    }

                    var comment = new RedditComment
                    {
                        RedditId = commentData.RedditId,
                        Post = post,
                        ParentRedditId = commentData.ParentRedditId,
                        Body = commentData.Body.Length > 4000 ? commentData.Body[..4000] : commentData.Body,
                        CreatedAt = commentData.CreatedAt,
                        Score = commentData.Score
                    };

                    db.RedditComments.Add(comment);
                    await db.SaveChangesAsync(cancellationToken);
                    insertedComments++;

                    detectedMentions += await AddMentionsForCommentAsync(comment, players, cancellationToken);
                }
            }
        }

        logger.LogInformation(
            "Reddit ingestion completed. New comments: {CommentCount}. Player mentions: {MentionCount}.",
            insertedComments,
            detectedMentions);

        return detectedMentions;
    }

    private async Task<RedditPost> UpsertPostAsync(RedditPostData postData, CancellationToken cancellationToken)
    {
        var post = await db.RedditPosts
            .FirstOrDefaultAsync(existing => existing.RedditId == postData.RedditId, cancellationToken);

        if (post is not null)
        {
            post.CommentCount = postData.CommentCount;
            post.Title = postData.Title;
            post.Url = postData.Url;
            return post;
        }

        post = new RedditPost
        {
            RedditId = postData.RedditId,
            Subreddit = postData.Subreddit,
            Title = postData.Title,
            CreatedAt = postData.CreatedAt,
            Url = postData.Url,
            CommentCount = postData.CommentCount
        };

        db.RedditPosts.Add(post);
        await db.SaveChangesAsync(cancellationToken);
        return post;
    }

    private async Task<int> AddMentionsForCommentAsync(
        RedditComment comment,
        IReadOnlyList<Player> players,
        CancellationToken cancellationToken)
    {
        var matches = mentionDetector.DetectMentions(comment.Body, players);
        var voteWeight = voteWeightCalculator.Calculate(comment.Score);
        var added = 0;

        foreach (var match in matches)
        {
            var alreadyExists = await db.PlayerMentions.AnyAsync(
                mention => mention.CommentId == comment.Id && mention.PlayerId == match.PlayerId,
                cancellationToken);

            if (alreadyExists)
            {
                continue;
            }

            var context = contextExtractor.ExtractContext(comment.Body, match);
            var sentiment = await sentimentAnalyzer.AnalyzeAsync(
                new TargetedSentimentRequest(comment.Body, context, match),
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

    private async Task RefreshExistingCommentScoreAsync(
        RedditComment existingComment,
        int latestScore,
        CancellationToken cancellationToken)
    {
        if (existingComment.Score == latestScore)
        {
            return;
        }

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

    private static string? Truncate(string? value, int maxLength)
    {
        return string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
}
