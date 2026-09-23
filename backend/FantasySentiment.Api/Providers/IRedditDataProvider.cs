namespace FantasySentiment.Api.Providers;

public interface IRedditDataProvider
{
    Task<IReadOnlyList<RedditPostData>> FetchRecentPostsAsync(
        string subreddit,
        DateTimeOffset since,
        CancellationToken cancellationToken);
}

public sealed record RedditPostData(
    string RedditId,
    string Subreddit,
    string Title,
    DateTimeOffset CreatedAt,
    string? Url,
    int CommentCount,
    IReadOnlyList<RedditCommentData> Comments);

public sealed record RedditCommentData(
    string RedditId,
    string? ParentRedditId,
    string Body,
    DateTimeOffset CreatedAt,
    int Score);
