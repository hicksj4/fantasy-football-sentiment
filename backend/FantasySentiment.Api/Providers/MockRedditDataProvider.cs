namespace FantasySentiment.Api.Providers;

public sealed class MockRedditDataProvider(
    ILogger<MockRedditDataProvider> logger) : IRedditDataProvider
{
    private readonly Lock _lock = new();
    private int _discoveryCycle;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow.AddHours(-3);

    public Task<IReadOnlyList<RedditPostData>> DiscoverRecentPostsAsync(
        string subreddit,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        int cycle;
        lock (_lock)
        {
            _discoveryCycle++;
            cycle = _discoveryCycle;
        }

        var posts = BuildPosts(subreddit, cycle)
            .Where(post => post.CreatedAt >= since)
            .ToList();

        logger.LogInformation(
            "Mock reddit discovery cycle {Cycle} for r/{Subreddit}. Returned {PostCount} posts.",
            cycle,
            subreddit,
            posts.Count);

        return Task.FromResult<IReadOnlyList<RedditPostData>>(posts);
    }

    public Task<IReadOnlyList<RedditCommentData>> FetchCommentsAsync(
        string subreddit,
        string postRedditId,
        DateTimeOffset postCreatedAt,
        CancellationToken cancellationToken)
    {
        int cycle;
        lock (_lock)
        {
            cycle = Math.Max(1, _discoveryCycle);
        }

        var comments = BuildComments(postRedditId, postCreatedAt, cycle);
        logger.LogInformation(
            "Mock reddit comment fetch for {PostId}. Returned {CommentCount} compact comments.",
            postRedditId,
            comments.Count);

        return Task.FromResult<IReadOnlyList<RedditCommentData>>(comments);
    }

    private IReadOnlyList<RedditPostData> BuildPosts(string subreddit, int cycle)
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            Post("mock-low-activity", subreddit, "Who are your deepest bench stashes?", now.AddMinutes(-45), 4, 10),
            Post("mock-growing-normal", subreddit, "Week 4 panic meter and waiver overreactions", now.AddMinutes(-80), cycle >= 2 ? 66 : 28, cycle >= 2 ? 122 : 72),
            Post("mock-injury-news", subreddit, "Injury update: star WR questionable after practice", now.AddMinutes(-25), 18, 35),
            Post("mock-game-thread", subreddit, "GAME THREAD: Sunday afternoon fantasy football chaos", _startedAt, 230, GameThreadCount(cycle)),
            Post("mock-incremental", subreddit, "Start sit help and lineup reactions", now.AddHours(-2), 75, cycle >= 3 ? 132 : 118)
        ];
    }

    private static RedditPostData Post(
        string id,
        string subreddit,
        string title,
        DateTimeOffset createdAt,
        int score,
        int commentCount)
    {
        return new RedditPostData(
            id,
            subreddit,
            title,
            createdAt,
            $"https://reddit.com/r/{subreddit}/comments/{id}",
            score,
            commentCount);
    }

    private static int GameThreadCount(int cycle)
    {
        return cycle switch
        {
            <= 1 => 800,
            2 => 2700,
            3 => 4800,
            4 => 5900,
            5 => 6040,
            6 => 6061,
            _ => 6067
        };
    }

    private static IReadOnlyList<RedditCommentData> BuildComments(
        string postRedditId,
        DateTimeOffset postCreatedAt,
        int cycle)
    {
        return postRedditId switch
        {
            "mock-growing-normal" => NormalDiscussionComments(postRedditId, postCreatedAt, cycle),
            "mock-injury-news" => InjuryNewsComments(postRedditId, postCreatedAt, cycle),
            "mock-game-thread" => GameThreadComments(postRedditId, postCreatedAt, cycle),
            "mock-incremental" => IncrementalComments(postRedditId, postCreatedAt, cycle),
            _ => LowActivityComments(postRedditId, postCreatedAt, cycle)
        };
    }

    private static IReadOnlyList<RedditCommentData> LowActivityComments(string postId, DateTimeOffset createdAt, int cycle)
    {
        return
        [
            Comment(postId, "c1", null, "Rome Odunze is a sneaky add if benches are deep.", createdAt, 4 + cycle),
            Comment(postId, "c2", null, "Is Jefferson hurt or just getting normal rest?", createdAt, 2)
        ];
    }

    private static IReadOnlyList<RedditCommentData> NormalDiscussionComments(string postId, DateTimeOffset createdAt, int cycle)
    {
        return
        [
            Comment(postId, "c1", null, "CMC is a beast, love him. Absolute league winner.", createdAt, 5000 + cycle),
            Comment(postId, "c2", null, "Jefferson is actually cooked and unstartable until that offense wakes up.", createdAt, 5000),
            Comment(postId, "c3", null, "CMC looked great but Purdy was awful.", createdAt, 22),
            Comment(postId, "c4", null, "I love Breece but I'm selling high.", createdAt, 16)
        ];
    }

    private static IReadOnlyList<RedditCommentData> InjuryNewsComments(string postId, DateTimeOffset createdAt, int cycle)
    {
        return
        [
            Comment(postId, "c1", null, "If Puka is out, Kupp is a smash start.", createdAt, 45 + cycle),
            Comment(postId, "c2", null, "Is Jefferson hurt? This feels neutral until the report lands.", createdAt, 8),
            Comment(postId, "c3", null, "Tank Bigsby is a must add if the starter misses time.", createdAt, 13)
        ];
    }

    private static IReadOnlyList<RedditCommentData> GameThreadComments(string postId, DateTimeOffset createdAt, int cycle)
    {
        return
        [
            Comment(postId, "c1", null, "Puka looks absolutely elite today. Monster usage.", createdAt, 101 + cycle),
            Comment(postId, "c2", null, "Drake London goose egg has me tilted. Sit until they prove it.", createdAt, 88),
            Comment(postId, "c3", null, "Trey McBride is a TE1 and somehow still underrated.", createdAt, 62),
            Comment(postId, "c4", null, "Jayden Reed is cooking. Waiver steal if your league slept.", createdAt, 58)
        ];
    }

    private static IReadOnlyList<RedditCommentData> IncrementalComments(string postId, DateTimeOffset createdAt, int cycle)
    {
        var comments = new List<RedditCommentData>
        {
            Comment(postId, "c1", null, "Garrett Wilson usage is still elite.", createdAt, 20 + cycle),
            Comment(postId, "c2", null, "James Cook is a sell high after this spike week.", createdAt, 18)
        };

        if (cycle >= 3)
        {
            comments.Add(Comment(postId, "c3", "c1", "Agreed. Garrett Wilson is a monster if the targets hold.", createdAt.AddMinutes(30), 7));
        }

        return comments;
    }

    private static RedditCommentData Comment(
        string postId,
        string commentId,
        string? parentCommentId,
        string body,
        DateTimeOffset postCreatedAt,
        int score)
    {
        return new RedditCommentData(
            $"{postId}-{commentId}",
            parentCommentId is null ? null : $"{postId}-{parentCommentId}",
            body,
            postCreatedAt.AddMinutes(Math.Abs(commentId.GetHashCode()) % 90),
            score);
    }
}
