using FantasySentiment.Api.Domain;

namespace FantasySentiment.Api.Services;

public sealed record SnapshotSourceData(
    string Key,
    string Name,
    int Season,
    int Week,
    IReadOnlyList<SnapshotSourcePost> Posts);

public sealed record SnapshotSourcePost(
    string RedditId,
    string Title,
    DateTimeOffset CreatedAt,
    int Score,
    IReadOnlyList<SnapshotSourceComment> Comments,
    string Subreddit = "fantasyfootball",
    string PostType = RedditPostTypes.GameThread);

public sealed record SnapshotSourceComment(
    string RedditId,
    string? ParentRedditId,
    DateTimeOffset CreatedAt,
    int Score,
    string Body);

public interface ISnapshotSource
{
    Task<SnapshotSourceData> LoadAsync(string key, CancellationToken cancellationToken);
}
