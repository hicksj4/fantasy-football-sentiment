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
    FantasySentimentAnalyzer sentimentAnalyzer,
    FantasyIntentClassifier intentClassifier,
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
        var sentiment = sentimentAnalyzer.Analyze(comment.Body);
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

            db.PlayerMentions.Add(new PlayerMention
            {
                PlayerId = match.PlayerId,
                CommentId = comment.Id,
                CreatedAt = comment.CreatedAt,
                SentimentScore = sentiment.Score,
                SentimentCategory = sentiment.Category,
                Intent = intentClassifier.Classify(comment.Body, match)
            });
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }
}
