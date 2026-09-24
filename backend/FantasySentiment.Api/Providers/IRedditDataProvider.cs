namespace FantasySentiment.Api.Providers;

public interface IRedditDataProvider
{
    Task<IReadOnlyList<RedditPostData>> DiscoverRecentPostsAsync(
        string subreddit,
        DateTimeOffset since,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RedditCommentData>> FetchCommentsAsync(
        string subreddit,
        string postRedditId,
        DateTimeOffset postCreatedAt,
        CancellationToken cancellationToken);
}

public sealed record RedditPostData(
    string RedditId,
    string Subreddit,
    string Title,
    DateTimeOffset CreatedAt,
    string? Url,
    int Score,
    int CommentCount);

public sealed record RedditCommentData(
    string RedditId,
    string? ParentRedditId,
    string Body,
    DateTimeOffset CreatedAt,
    int Score);
