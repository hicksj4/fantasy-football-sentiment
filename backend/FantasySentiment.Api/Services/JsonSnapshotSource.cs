using System.Text.Json;
using FantasySentiment.Api.Options;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Services;

public sealed class JsonSnapshotSource(
    IWebHostEnvironment environment,
    IOptions<SnapshotProcessingOptions> options,
    ILogger<JsonSnapshotSource> logger) : ISnapshotSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SnapshotProcessingOptions _options = options.Value;

    public async Task<SnapshotSourceData> LoadAsync(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Snapshot key is required.");
        }

        var path = ResolvePath(key);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Snapshot fixture '{key}' was not found at '{path}'.", path);
        }

        logger.LogInformation("Loading snapshot {SnapshotKey} from {Path}.", key, path);

        await using var stream = File.OpenRead(path);
        SnapshotFixture? fixture;
        try
        {
            fixture = await JsonSerializer.DeserializeAsync<SnapshotFixture>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Snapshot fixture '{key}' is not valid JSON: {ex.Message}", ex);
        }

        if (fixture is null)
        {
            throw new InvalidOperationException($"Snapshot fixture '{key}' is empty.");
        }

        var source = new SnapshotSourceData(
            fixture.Key?.Trim() ?? string.Empty,
            fixture.Name?.Trim() ?? string.Empty,
            fixture.Season,
            fixture.Week,
            fixture.Posts
                .Select(post => new SnapshotSourcePost(
                    post.RedditId?.Trim() ?? string.Empty,
                    post.Title?.Trim() ?? string.Empty,
                    post.CreatedAt,
                    post.Score,
                    post.Comments
                        .Select(comment => new SnapshotSourceComment(
                            comment.RedditId?.Trim() ?? string.Empty,
                            string.IsNullOrWhiteSpace(comment.ParentRedditId) ? null : comment.ParentRedditId.Trim(),
                            comment.CreatedAt,
                            comment.Score,
                            comment.Body?.Trim() ?? string.Empty))
                        .ToList(),
                    NormalizeSubreddit(post.Subreddit)))
                .ToList());

        Validate(source, key);
        return source;
    }

    private string ResolvePath(string key)
    {
        var relativeDirectory = _options.FixtureDirectory.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(environment.ContentRootPath, relativeDirectory, $"{key}.json");
    }

    private static void Validate(SnapshotSourceData snapshot, string requestedKey)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(snapshot.Key))
        {
            errors.Add("Snapshot key is required.");
        }
        else if (!snapshot.Key.Equals(requestedKey, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Snapshot key '{snapshot.Key}' does not match requested key '{requestedKey}'.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.Name))
        {
            errors.Add("Snapshot name is required.");
        }

        if (snapshot.Season <= 0)
        {
            errors.Add("Snapshot season must be a positive year.");
        }

        if (snapshot.Week <= 0)
        {
            errors.Add("Snapshot week must be positive.");
        }

        if (snapshot.Posts.Count == 0)
        {
            errors.Add("Snapshot must contain at least one post.");
        }

        var postIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commentsByPost = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var post in snapshot.Posts)
        {
            if (string.IsNullOrWhiteSpace(post.RedditId))
            {
                errors.Add("Every post must have a redditId.");
                continue;
            }

            if (!postIds.Add(post.RedditId))
            {
                errors.Add($"Duplicate post redditId '{post.RedditId}'.");
            }

            if (string.IsNullOrWhiteSpace(post.Title))
            {
                errors.Add($"Post '{post.RedditId}' must have a title.");
            }

            if (string.IsNullOrWhiteSpace(post.Subreddit))
            {
                errors.Add($"Post '{post.RedditId}' must have a subreddit.");
            }

            if (post.Comments.Count == 0)
            {
                errors.Add($"Post '{post.RedditId}' must contain at least one comment.");
            }

            var postCommentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            commentsByPost[post.RedditId] = postCommentIds;

            foreach (var comment in post.Comments)
            {
                if (string.IsNullOrWhiteSpace(comment.RedditId))
                {
                    errors.Add($"Post '{post.RedditId}' contains a comment without a redditId.");
                    continue;
                }

                if (!commentIds.Add(comment.RedditId))
                {
                    errors.Add($"Duplicate comment redditId '{comment.RedditId}'.");
                }

                postCommentIds.Add(comment.RedditId);

                if (string.IsNullOrWhiteSpace(comment.Body))
                {
                    errors.Add($"Comment '{comment.RedditId}' must have a body.");
                }
            }
        }

        foreach (var post in snapshot.Posts)
        {
            var validParentIds = commentsByPost[post.RedditId];
            foreach (var comment in post.Comments.Where(comment => !string.IsNullOrWhiteSpace(comment.ParentRedditId)))
            {
                if (!validParentIds.Contains(comment.ParentRedditId!))
                {
                    errors.Add(
                        $"Comment '{comment.RedditId}' references parent '{comment.ParentRedditId}', which is not in post '{post.RedditId}'.");
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Snapshot fixture '{requestedKey}' failed validation: {string.Join(" ", errors)}");
        }
    }

    private static string NormalizeSubreddit(string? subreddit)
    {
        var normalized = subreddit?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "fantasyfootball";
        }

        return normalized.StartsWith("r/", StringComparison.OrdinalIgnoreCase)
            ? normalized[2..]
            : normalized;
    }

    private sealed record SnapshotFixture(
        string? Key,
        string? Name,
        int Season,
        int Week,
        IReadOnlyList<SnapshotPostFixture> Posts)
    {
        public IReadOnlyList<SnapshotPostFixture> Posts { get; init; } = Posts ?? [];
    }

    private sealed record SnapshotPostFixture(
        string? RedditId,
        string? Subreddit,
        string? Title,
        DateTimeOffset CreatedAt,
        int Score,
        IReadOnlyList<SnapshotCommentFixture> Comments)
    {
        public IReadOnlyList<SnapshotCommentFixture> Comments { get; init; } = Comments ?? [];
    }

    private sealed record SnapshotCommentFixture(
        string? RedditId,
        string? ParentRedditId,
        DateTimeOffset CreatedAt,
        int Score,
        string? Body);
}
