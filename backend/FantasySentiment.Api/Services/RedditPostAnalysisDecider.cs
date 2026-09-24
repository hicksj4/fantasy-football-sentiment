using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace FantasySentiment.Api.Services;

public sealed class RedditPostAnalysisDecider(IOptions<RedditAnalysisOptions> options)
{
    private readonly RedditAnalysisOptions _options = options.Value;

    public RedditPostAnalysisDecision Decide(RedditPost post, DateTimeOffset now)
    {
        var postType = DeterminePostType(post.Title);
        var newCommentsSinceAnalysis = Math.Max(0, post.CommentCount - post.LastAnalyzedCommentCount);

        if (post.AnalysisStatus == RedditAnalysisStatuses.Analyzing)
        {
            return new RedditPostAnalysisDecision(
                RedditAnalysisStatuses.Analyzing,
                postType,
                "analysis already in progress",
                newCommentsSinceAnalysis);
        }

        if (post.LastAnalyzedAt is not null)
        {
            if (newCommentsSinceAnalysis >= Math.Max(1, _options.IncrementalMinimumNewComments))
            {
                return new RedditPostAnalysisDecision(
                    RedditAnalysisStatuses.ReadyForAnalysis,
                    postType,
                    $"incremental threshold reached: {newCommentsSinceAnalysis} new comments",
                    newCommentsSinceAnalysis);
            }

            return new RedditPostAnalysisDecision(
                RedditAnalysisStatuses.Analyzed,
                postType,
                $"already analyzed; {newCommentsSinceAnalysis} new comments below incremental threshold",
                newCommentsSinceAnalysis);
        }

        return postType switch
        {
            RedditPostTypes.GameThread => DecideGameThread(post, now, postType, newCommentsSinceAnalysis),
            RedditPostTypes.ImportantNews => DecideImportantNews(post, postType, newCommentsSinceAnalysis),
            _ => DecideNormalPost(post, postType, newCommentsSinceAnalysis)
        };
    }

    public string DeterminePostType(string title)
    {
        if (ContainsAny(title, _options.GameThreads.TitlePatterns))
        {
            return RedditPostTypes.GameThread;
        }

        return ContainsAny(title, _options.ImportantNews.Keywords)
            ? RedditPostTypes.ImportantNews
            : RedditPostTypes.Normal;
    }

    private RedditPostAnalysisDecision DecideNormalPost(
        RedditPost post,
        string postType,
        int newCommentsSinceAnalysis)
    {
        var normal = _options.NormalPosts;
        if (post.CommentCount >= normal.MinimumComments)
        {
            return Ready(postType, $"normal discussion threshold reached: {post.CommentCount} comments", newCommentsSinceAnalysis);
        }

        if (post.CommentCount >= normal.MinimumCommentsWithScore && post.Score >= normal.MinimumPostScore)
        {
            return Ready(
                postType,
                $"normal score threshold reached: {post.CommentCount} comments and score {post.Score}",
                newCommentsSinceAnalysis);
        }

        return Collecting(
            postType,
            $"below normal threshold: {post.CommentCount} comments, score {post.Score}",
            newCommentsSinceAnalysis);
    }

    private RedditPostAnalysisDecision DecideImportantNews(
        RedditPost post,
        string postType,
        int newCommentsSinceAnalysis)
    {
        if (post.CommentCount >= _options.ImportantNews.MinimumComments)
        {
            return Ready(
                postType,
                $"important-news threshold reached: {post.CommentCount} comments",
                newCommentsSinceAnalysis);
        }

        return Collecting(
            postType,
            $"below important-news threshold: {post.CommentCount} comments",
            newCommentsSinceAnalysis);
    }

    private RedditPostAnalysisDecision DecideGameThread(
        RedditPost post,
        DateTimeOffset now,
        string postType,
        int newCommentsSinceAnalysis)
    {
        var game = _options.GameThreads;
        var ageMinutes = (now - post.CreatedAt).TotalMinutes;
        if (post.CommentCount < game.MinimumComments)
        {
            return Collecting(
                postType,
                $"game thread below minimum comments: {post.CommentCount}",
                newCommentsSinceAnalysis);
        }

        if (ageMinutes < game.MinimumAgeMinutes)
        {
            return Collecting(
                postType,
                $"game thread age {ageMinutes:0}m below minimum {game.MinimumAgeMinutes}m",
                newCommentsSinceAnalysis);
        }

        if (post.LastObservedCommentCountAt is null)
        {
            return Collecting(postType, "game thread needs a second observation before cooldown can be evaluated", newCommentsSinceAnalysis);
        }

        var cooldownElapsedMinutes = (now - post.LastObservedCommentCountAt.Value).TotalMinutes;
        var newCommentsInCooldown = Math.Max(0, post.CommentCount - post.PreviousCommentCount);
        if (cooldownElapsedMinutes < game.CooldownWindowMinutes)
        {
            return Collecting(
                postType,
                $"game cooldown window still active: {newCommentsInCooldown} new comments over {cooldownElapsedMinutes:0}m",
                newCommentsSinceAnalysis);
        }

        if (newCommentsInCooldown <= game.MaximumNewCommentsDuringCooldown)
        {
            return Ready(
                postType,
                $"game activity cooled: {newCommentsInCooldown} new comments over {cooldownElapsedMinutes:0}m",
                newCommentsSinceAnalysis);
        }

        return Collecting(
            postType,
            $"game still active: {newCommentsInCooldown} new comments over {cooldownElapsedMinutes:0}m",
            newCommentsSinceAnalysis);
    }

    private static RedditPostAnalysisDecision Ready(string postType, string reason, int newCommentsSinceAnalysis)
    {
        return new RedditPostAnalysisDecision(RedditAnalysisStatuses.ReadyForAnalysis, postType, reason, newCommentsSinceAnalysis);
    }

    private static RedditPostAnalysisDecision Collecting(string postType, string reason, int newCommentsSinceAnalysis)
    {
        return new RedditPostAnalysisDecision(RedditAnalysisStatuses.Collecting, postType, reason, newCommentsSinceAnalysis);
    }

    private static bool ContainsAny(string value, IEnumerable<string> patterns)
    {
        return patterns.Any(pattern =>
            !string.IsNullOrWhiteSpace(pattern)
            && ContainsPattern(value, pattern));
    }

    private static bool ContainsPattern(string value, string pattern)
    {
        if (pattern.Any(char.IsWhiteSpace))
        {
            return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        return Regex.IsMatch(
            value,
            $@"\b{Regex.Escape(pattern)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

public sealed record RedditPostAnalysisDecision(
    string Status,
    string PostType,
    string Reason,
    int NewCommentsSinceLastAnalysis);
