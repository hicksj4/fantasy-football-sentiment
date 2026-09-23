namespace FantasySentiment.Api.Providers;

public sealed class MockRedditDataProvider(
    ILogger<MockRedditDataProvider> logger) : IRedditDataProvider
{
    private readonly Random _random = new(42);
    private long _sequence;

    public Task<IReadOnlyList<RedditPostData>> FetchRecentPostsAsync(
        string subreddit,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var comments = BuildCommentBatch(subreddit, now);
        var postId = $"mockpost-{Interlocked.Increment(ref _sequence)}";

        logger.LogInformation(
            "Mock reddit fetch completed for r/{Subreddit}. Generated {CommentCount} comments.",
            subreddit,
            comments.Count);

        IReadOnlyList<RedditPostData> posts =
        [
            new RedditPostData(
                postId,
                subreddit,
                "Game day reaction thread: waiver panic and start/sit chaos",
                now,
                $"https://reddit.com/r/{subreddit}/comments/{postId}",
                comments.Count,
                comments)
        ];

        return Task.FromResult(posts);
    }

    private List<RedditCommentData> BuildCommentBatch(string subreddit, DateTimeOffset now)
    {
        string[] templates =
        [
            "Puka looks absolutely elite today. League winner energy.",
            "Should I drop Mark Andrews for Trey McBride? Andrews looks washed.",
            "Tank Bigsby is a must add if he is still on waivers.",
            "CMC is still an automatic smash start. RB1 forever.",
            "Kyle Pitts is unstartable. I am selling if anyone bites.",
            "Malik Nabers is cooking. Total breakout game.",
            "Is De'Von Achane hurt or just limited? I am nervous.",
            "Bijan is a monster. Buy high, I do not care.",
            "Start Zay Flowers or Jaylen Waddle this week?",
            "I am holding Garrett Wilson. Usage is still elite.",
            "Rome Odunze feels like a sneaky add before the hype hits.",
            "Breece Hall panic is overblown. Still a beast.",
            "Drake London goose egg has me tilted. Bench until they prove it.",
            "Trey McBride is a TE1 and somehow still underrated.",
            "Jayden Reed is balling. Waiver steal if your league slept.",
            "Selling high on James Cook after this spike week."
        ];

        var count = _random.Next(12, 24);
        var comments = new List<RedditCommentData>(count);

        for (var i = 0; i < count; i++)
        {
            var body = templates[_random.Next(templates.Length)];
            if (_random.NextDouble() < 0.25)
            {
                body += " Anyone else seeing this?";
            }

            var id = $"mockc-{Interlocked.Increment(ref _sequence)}";
            comments.Add(new RedditCommentData(
                id,
                null,
                body,
                now.AddSeconds(-_random.Next(0, 240)),
                _random.Next(-3, 42)));
        }

        return comments;
    }
}
