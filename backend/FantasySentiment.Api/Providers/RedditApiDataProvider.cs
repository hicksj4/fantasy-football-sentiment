using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FantasySentiment.Api.Options;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Providers;

public sealed class RedditApiDataProvider(
    HttpClient httpClient,
    IOptions<RedditOptions> options,
    ILogger<RedditApiDataProvider> logger) : IRedditDataProvider
{
    private readonly RedditOptions _options = options.Value;
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public async Task<IReadOnlyList<RedditPostData>> DiscoverRecentPostsAsync(
        string subreddit,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            logger.LogWarning("Reddit API provider selected, but credentials are missing. Returning no posts.");
            return [];
        }

        await EnsureAccessTokenAsync(cancellationToken);

        logger.LogInformation("Reddit fetch started for r/{Subreddit}.", subreddit);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://oauth.reddit.com/r/{Uri.EscapeDataString(subreddit)}/new?limit=15&raw_json=1");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);

        using var response = await SendWithRetriesAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var posts = new List<RedditPostData>();
        foreach (var child in document.RootElement.GetProperty("data").GetProperty("children").EnumerateArray())
        {
            var data = child.GetProperty("data");
            var createdAt = DateTimeOffset.FromUnixTimeSeconds((long)data.GetProperty("created_utc").GetDouble());
            if (createdAt < since)
            {
                continue;
            }

            var redditId = data.GetProperty("id").GetString() ?? string.Empty;
            posts.Add(new RedditPostData(
                redditId,
                subreddit,
                data.GetProperty("title").GetString() ?? "(untitled)",
                createdAt,
                data.TryGetProperty("url", out var url) ? url.GetString() : null,
                data.TryGetProperty("score", out var score) ? score.GetInt32() : 0,
                data.TryGetProperty("num_comments", out var count) ? count.GetInt32() : 0));
        }

        logger.LogInformation(
            "Reddit fetch completed for r/{Subreddit}. Fetched {PostCount} posts.",
            subreddit,
            posts.Count);

        return posts;
    }

    public async Task<IReadOnlyList<RedditCommentData>> FetchCommentsAsync(
        string subreddit,
        string postId,
        DateTimeOffset postCreatedAt,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://oauth.reddit.com/r/{Uri.EscapeDataString(subreddit)}/comments/{postId}?limit=500&depth=8&raw_json=1");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);

        using var response = await SendWithRetriesAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.GetArrayLength() < 2)
        {
            return [];
        }

        var comments = new List<RedditCommentData>();
        var listing = document.RootElement[1].GetProperty("data").GetProperty("children");
        foreach (var child in listing.EnumerateArray())
        {
            AddCommentsRecursive(child, comments, postCreatedAt);
        }

        return comments;
    }

    private static void AddCommentsRecursive(
        JsonElement node,
        List<RedditCommentData> comments,
        DateTimeOffset fallbackCreatedAt)
    {
        if (!node.TryGetProperty("kind", out var kind) || kind.GetString() != "t1")
        {
            return;
        }

        var data = node.GetProperty("data");
        var body = data.TryGetProperty("body", out var bodyElement)
            ? bodyElement.GetString()
            : null;

        if (!string.IsNullOrWhiteSpace(body) && body is not "[deleted]" and not "[removed]")
        {
            var createdAt = data.TryGetProperty("created_utc", out var created)
                ? DateTimeOffset.FromUnixTimeSeconds((long)created.GetDouble())
                : fallbackCreatedAt;

            comments.Add(new RedditCommentData(
                data.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
                data.TryGetProperty("parent_id", out var parent) ? parent.GetString() : null,
                body.Length > 4000 ? body[..4000] : body,
                createdAt,
                data.TryGetProperty("score", out var score) ? score.GetInt32() : 0));
        }

        if (!data.TryGetProperty("replies", out var replies) || replies.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!replies.TryGetProperty("data", out var repliesData)
            || !repliesData.TryGetProperty("children", out var children))
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            AddCommentsRecursive(child, comments, fallbackCreatedAt);
        }
    }

    private async Task EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.reddit.com/api/v1/access_token");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        ]);

        using var response = await SendWithRetriesAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        _accessToken = document.RootElement.GetProperty("access_token").GetString();
        var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expires)
            ? expires.GetInt32()
            : 3600;
        _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
    }

    private async Task<HttpResponseMessage> SendWithRetriesAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var clone = await CloneRequestAsync(request, cancellationToken);
            var response = await httpClient.SendAsync(clone, cancellationToken);

            if (response.StatusCode != HttpStatusCode.TooManyRequests
                && (int)response.StatusCode < 500)
            {
                return response;
            }

            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt * 2);
            logger.LogWarning(
                "Reddit request throttled or failed with {StatusCode}. Retrying in {DelaySeconds}s.",
                response.StatusCode,
                delay.TotalSeconds);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }

        return await httpClient.SendAsync(await CloneRequestAsync(request, cancellationToken), cancellationToken);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
