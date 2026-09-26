using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using FantasySentiment.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Tests;

public sealed class SnapshotProcessingTests
{
    [Fact]
    public async Task JsonSnapshotSource_ParsesValidFixture()
    {
        using var fixture = await SnapshotFixtureDirectory.CreateAsync(
            "parse-test",
            """
            {
              "key": "parse-test",
              "name": "Parse Test",
              "season": 2026,
              "week": 4,
              "posts": [
                {
                  "redditId": "post-a",
                  "subreddit": "minnesotavikings",
                  "title": "Thread A",
                  "createdAt": "2026-09-20T17:00:00Z",
                  "score": 12,
                  "comments": [
                    {
                      "redditId": "comment-a",
                      "parentRedditId": null,
                      "createdAt": "2026-09-20T17:01:00Z",
                      "score": 5,
                      "body": "CMC is elite."
                    }
                  ]
                }
              ]
            }
            """);

        var source = CreateJsonSource(fixture.Root);
        var snapshot = await source.LoadAsync("parse-test", CancellationToken.None);

        Assert.Equal("parse-test", snapshot.Key);
        Assert.Single(snapshot.Posts);
        Assert.Equal("minnesotavikings", snapshot.Posts[0].Subreddit);
        Assert.Single(snapshot.Posts[0].Comments);
    }

    [Fact]
    public async Task JsonSnapshotSource_RejectsDuplicateCommentIds()
    {
        using var fixture = await SnapshotFixtureDirectory.CreateAsync(
            "bad-duplicates",
            """
            {
              "key": "bad-duplicates",
              "name": "Bad Duplicates",
              "season": 2026,
              "week": 4,
              "posts": [
                {
                  "redditId": "post-a",
                  "title": "Thread A",
                  "createdAt": "2026-09-20T17:00:00Z",
                  "score": 12,
                  "comments": [
                    { "redditId": "dup", "parentRedditId": null, "createdAt": "2026-09-20T17:01:00Z", "score": 1, "body": "CMC is elite." },
                    { "redditId": "dup", "parentRedditId": null, "createdAt": "2026-09-20T17:02:00Z", "score": 1, "body": "Jefferson is washed." }
                  ]
                }
              ]
            }
            """);

        var source = CreateJsonSource(fixture.Root);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.LoadAsync("bad-duplicates", CancellationToken.None));

        Assert.Contains("Duplicate comment redditId", error.Message);
    }

    [Fact]
    public async Task JsonSnapshotSource_RejectsMissingParents()
    {
        using var fixture = await SnapshotFixtureDirectory.CreateAsync(
            "bad-parent",
            """
            {
              "key": "bad-parent",
              "name": "Bad Parent",
              "season": 2026,
              "week": 4,
              "posts": [
                {
                  "redditId": "post-a",
                  "title": "Thread A",
                  "createdAt": "2026-09-20T17:00:00Z",
                  "score": 12,
                  "comments": [
                    { "redditId": "comment-a", "parentRedditId": "missing", "createdAt": "2026-09-20T17:01:00Z", "score": 1, "body": "CMC is elite." }
                  ]
                }
              ]
            }
            """);

        var source = CreateJsonSource(fixture.Root);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.LoadAsync("bad-parent", CancellationToken.None));

        Assert.Contains("references parent", error.Message);
    }

    [Fact]
    public async Task SnapshotProcessing_IsIdempotent()
    {
        await using var db = CreateDb();
        SeedPlayers(db);
        var processor = CreateProcessor(db, StandardSource());

        await processor.ProcessAsync("2026-week-4", skipIfProcessed: false, CancellationToken.None);
        await processor.ProcessAsync("2026-week-4", skipIfProcessed: false, CancellationToken.None);

        Assert.Equal(1, await db.AnalysisSnapshots.CountAsync());
        Assert.Equal(3, await db.RedditPosts.CountAsync(post => post.SnapshotId != null));
        Assert.Equal(9, await db.RedditComments.CountAsync());
        Assert.Equal(9, await db.PlayerMentions.CountAsync());
        Assert.Equal(4, await db.SnapshotPlayerAggregates.CountAsync());
    }

    [Fact]
    public async Task SnapshotProcessing_ReprocessesWhenProcessedSnapshotDataIsMissing()
    {
        await using var db = CreateDb();
        SeedPlayers(db);
        var processor = CreateProcessor(db, StandardSource());

        await processor.ProcessAsync("2026-week-4", skipIfProcessed: false, CancellationToken.None);

        db.SnapshotPlayerAggregates.RemoveRange(db.SnapshotPlayerAggregates);
        db.PlayerMentions.RemoveRange(db.PlayerMentions);
        db.RedditComments.RemoveRange(db.RedditComments);
        db.RedditPosts.RemoveRange(db.RedditPosts);
        await db.SaveChangesAsync();

        var staleSnapshot = await db.AnalysisSnapshots.SingleAsync();
        Assert.Equal(AnalysisSnapshotStatuses.Processed, staleSnapshot.Status);
        Assert.Equal(9, staleSnapshot.TotalComments);
        Assert.Empty(db.RedditComments);
        Assert.Empty(db.PlayerMentions);
        Assert.Empty(db.SnapshotPlayerAggregates);

        await processor.ProcessAsync("2026-week-4", skipIfProcessed: true, CancellationToken.None);

        Assert.Equal(1, await db.AnalysisSnapshots.CountAsync());
        Assert.Equal(3, await db.RedditPosts.CountAsync(post => post.SnapshotId == staleSnapshot.Id));
        Assert.Equal(9, await db.RedditComments.CountAsync(comment => comment.Post.SnapshotId == staleSnapshot.Id));
        Assert.Equal(9, await db.PlayerMentions.CountAsync(mention => mention.SnapshotId == staleSnapshot.Id));
        Assert.Equal(4, await db.SnapshotPlayerAggregates.CountAsync(aggregate => aggregate.SnapshotId == staleSnapshot.Id));
    }

    [Fact]
    public async Task SnapshotProcessing_AggregatesThreeSourcePostsIntoOneSnapshot()
    {
        await using var db = CreateDb();
        SeedPlayers(db);
        var processor = CreateProcessor(db, StandardSource());

        var snapshot = await processor.ProcessAsync("2026-week-4", skipIfProcessed: false, CancellationToken.None);

        Assert.Equal(3, snapshot.TotalPosts);
        Assert.Equal(9, snapshot.TotalComments);
        Assert.Equal(9, snapshot.TotalPlayerMentions);
        Assert.Equal(4, snapshot.UniquePlayersMentioned);
        Assert.Equal(AnalysisSnapshotStatuses.Processed, snapshot.Status);
    }

    [Fact]
    public async Task SnapshotProcessing_PlayerMentionsAcrossThreadsAggregateTogether()
    {
        await using var db = CreateDb();
        SeedPlayers(db);
        var processor = CreateProcessor(db, StandardSource());

        await processor.ProcessAsync("2026-week-4", skipIfProcessed: false, CancellationToken.None);

        var london = await AggregateForAsync(db, "Drake London");
        Assert.Equal(3, london.MentionCount);
        Assert.True(london.PositiveImpact > 0);
        Assert.True(london.DiscussionShare > 0);
    }

    [Fact]
    public async Task SnapshotProcessing_UsesVoteWeightingAcrossSnapshot()
    {
        await using var db = CreateDb();
        SeedPlayers(db);
        var processor = CreateProcessor(db, StandardSource());

        await processor.ProcessAsync("2026-week-4", skipIfProcessed: false, CancellationToken.None);

        var jefferson = await AggregateForAsync(db, "Justin Jefferson");
        Assert.True(jefferson.TotalVoteWeight < jefferson.TotalRedditScore);
        Assert.True(jefferson.CommunitySentimentScore < jefferson.SentimentScore);
    }

    [Fact]
    public async Task SnapshotHighlights_SelectTopPositiveAndNegativeImpact()
    {
        await using var db = CreateDb();
        SeedPlayers(db);
        var processor = CreateProcessor(db, StandardSource());
        await processor.ProcessAsync("2026-week-4", skipIfProcessed: false, CancellationToken.None);
        var query = CreateQuery(db);

        var highlights = await query.GetHighlightsAsync("2026-week-4", CancellationToken.None);

        Assert.NotNull(highlights);
        Assert.Equal("Drake London", highlights.Positive?.Name);
        Assert.Equal("Justin Jefferson", highlights.Negative?.Name);
        Assert.True(highlights.Positive?.Impact > 0);
        Assert.True(highlights.Negative?.Impact < 0);
    }

    [Fact]
    public async Task SnapshotHighlights_InsufficientSamplePlayerDoesNotWin()
    {
        await using var db = CreateDb();
        SeedPlayers(db);
        var processor = CreateProcessor(db, StandardSource(includeViralOneOff: true));
        await processor.ProcessAsync("2026-week-4", skipIfProcessed: false, CancellationToken.None);
        var query = CreateQuery(db, minimumMentions: 2);

        var highlights = await query.GetHighlightsAsync("2026-week-4", CancellationToken.None);

        Assert.NotEqual("One Comment Wonder", highlights?.Positive?.Name);
    }

    [Fact]
    public async Task SnapshotHighlights_SelectMostInfluentialDirectionalComment()
    {
        await using var db = CreateDb();
        SeedPlayers(db);
        var processor = CreateProcessor(db, StandardSource());
        await processor.ProcessAsync("2026-week-4", skipIfProcessed: false, CancellationToken.None);
        var query = CreateQuery(db);

        var highlights = await query.GetHighlightsAsync("2026-week-4", CancellationToken.None);

        Assert.Contains("monster league winner", highlights?.Positive?.TopInfluence?.CommentExcerpt);
        Assert.Contains("washed, awful, and terrible", highlights?.Negative?.TopInfluence?.CommentExcerpt);
        Assert.True(highlights?.Positive?.TopInfluence?.SentimentContribution > 0);
        Assert.True(highlights?.Negative?.TopInfluence?.SentimentContribution < 0);
    }

    [Fact]
    public async Task ParentInheritedTarget_BuildsSentimentContextFromParentAndChild()
    {
        await using var db = CreateDb();
        SeedPlayers(db);
        var analyzer = new CapturingSentimentAnalyzer();
        var processor = CreateProcessor(db, ParentInheritanceSource(), analyzer);

        await processor.ProcessAsync("context-test", skipIfProcessed: false, CancellationToken.None);

        var request = Assert.Single(analyzer.Requests, item => item.FullText == "Dropping this dude immediately.");
        Assert.Contains("Parent: Pitts got 2 targets.", request.Context);
        Assert.Contains("Comment: Dropping this dude immediately.", request.Context);
        Assert.Contains("Target context: Pitts got 2 targets. Dropping this dude immediately.", request.Context);
        Assert.Contains("Target: Kyle Pitts", request.Context);

        var mention = await db.PlayerMentions
            .Include(item => item.Player)
            .Include(item => item.Comment)
            .SingleAsync(item => item.Comment.Body == "Dropping this dude immediately.");
        Assert.Equal("Kyle Pitts", mention.Player.Name);
        Assert.Equal(TargetAttributionMethods.ParentInherited, mention.TargetAttributionMethod);
        Assert.Equal(0.90, mention.TargetConfidence);
        Assert.Equal(TargetPerspectives.Unknown, mention.TargetPerspective);
    }

    [Fact]
    public async Task SnapshotFixture_ProducesExpectedTargetAttributionBreakdown()
    {
        await using var db = CreateDb();
        var environment = new FakeEnvironment(AppContext.BaseDirectory);
        var seeder = new DemoDataSeeder(db, environment, NullLogger<DemoDataSeeder>.Instance);
        await seeder.SeedAsync(CancellationToken.None);
        var source = await CreateJsonSource(AppContext.BaseDirectory)
            .LoadAsync("2026-week-4", CancellationToken.None);
        var processor = CreateProcessor(db, source);

        await processor.ProcessAsync("2026-week-4", skipIfProcessed: false, CancellationToken.None);

        Assert.Equal(203, await db.RedditComments.CountAsync());
        Assert.Equal(208, await db.PlayerMentions.CountAsync());
        Assert.Equal(205, await CountMentionsByMethodAsync(db, TargetAttributionMethods.Explicit));
        Assert.Equal(2, await CountMentionsByMethodAsync(db, TargetAttributionMethods.ParentInherited));
        Assert.Equal(1, await CountMentionsByMethodAsync(db, TargetAttributionMethods.AncestorInherited));
        Assert.Equal(0, await CountMentionsByMethodAsync(db, TargetAttributionMethods.ThreadTitleInherited));
    }

    [Fact]
    public async Task ProcessingSameSnapshotTwice_DoesNotDuplicateAnalytics()
    {
        await using var db = CreateDb();
        SeedPlayers(db);
        var processor = CreateProcessor(db, StandardSource());

        await processor.ProcessAsync("2026-week-4", skipIfProcessed: false, CancellationToken.None);
        var firstMentionCount = await db.PlayerMentions.CountAsync();
        var firstAggregateCount = await db.SnapshotPlayerAggregates.CountAsync();
        await processor.ProcessAsync("2026-week-4", skipIfProcessed: false, CancellationToken.None);

        Assert.Equal(firstMentionCount, await db.PlayerMentions.CountAsync());
        Assert.Equal(firstAggregateCount, await db.SnapshotPlayerAggregates.CountAsync());
    }

    private static FantasySentimentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<FantasySentimentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FantasySentimentDbContext(options);
    }

    private static void SeedPlayers(FantasySentimentDbContext db)
    {
        db.Players.AddRange(
            Player("Christian McCaffrey", "SF", "RB", "CMC"),
            Player("Justin Jefferson", "MIN", "WR", "Jefferson", "JJettas"),
            Player("Drake London", "ATL", "WR", "London", "Drake London"),
            Player("Tank Bigsby", "JAX", "RB", "Tank", "Tank Bigsby"),
            Player("Kyle Pitts", "ATL", "TE", "Pitts"),
            Player("One Comment Wonder", "FA", "WR", "One Comment Wonder"));
        db.SaveChanges();
    }

    private static Player Player(string name, string team, string position, params string[] aliases)
    {
        return new Player
        {
            Name = name,
            Team = team,
            Position = position,
            IsActive = true,
            Aliases = aliases.Select(alias => new PlayerAlias { Alias = alias }).ToList()
        };
    }

    private static SnapshotProcessingService CreateProcessor(
        FantasySentimentDbContext db,
        SnapshotSourceData source,
        ISentimentAnalyzer? sentimentAnalyzer = null)
    {
        return new SnapshotProcessingService(
            db,
            new StaticSnapshotSource(source),
            new CommentTargetResolver(
                new PlayerMentionDetector(),
                NullLogger<CommentTargetResolver>.Instance),
            sentimentAnalyzer
                ?? new RuleBasedFantasySentimentAnalyzer(new FantasySentimentAnalyzer(new TestOptionsMonitor<SentimentLexiconOptions>(Lexicon()))),
            new TargetContextBuilder(new MentionContextExtractor()),
            new ThreadContextBuilder(),
            new FantasyIntentClassifier(new TestOptionsMonitor<IntentLexiconOptions>(Intents())),
            new VoteWeightCalculator(),
            NullLogger<SnapshotProcessingService>.Instance);
    }

    private static SnapshotQueryService CreateQuery(
        FantasySentimentDbContext db,
        int minimumMentions = 1)
    {
        return new SnapshotQueryService(
            db,
            Options.Create(new DashboardHighlightOptions
            {
                MinimumMentions = minimumMentions,
                MinimumSampleStrength = 0,
                ExcludeLowConfidence = false,
                MinimumAbsoluteSentimentChange = 0.01,
                CommentExcerptLength = 190
            }));
    }

    private static JsonSnapshotSource CreateJsonSource(string root)
    {
        return new JsonSnapshotSource(
            new FakeEnvironment(root),
            Options.Create(new SnapshotProcessingOptions
            {
                FixtureDirectory = "SeedData/Snapshots"
            }),
            NullLogger<JsonSnapshotSource>.Instance);
    }

    private static async Task<SnapshotPlayerAggregate> AggregateForAsync(
        FantasySentimentDbContext db,
        string playerName)
    {
        return await db.SnapshotPlayerAggregates
            .Include(aggregate => aggregate.Player)
            .SingleAsync(aggregate => aggregate.Player.Name == playerName);
    }

    private static Task<int> CountMentionsByMethodAsync(
        FantasySentimentDbContext db,
        string method)
    {
        return db.PlayerMentions.CountAsync(mention => mention.TargetAttributionMethod == method);
    }

    private static SnapshotSourceData StandardSource(bool includeViralOneOff = false)
    {
        var mondayComments = new List<SnapshotSourceComment>
        {
            Comment("mon-1", null, 50, "Drake London is a stud after this breakout."),
            Comment("mon-2", null, 150, "JJettas is a panic sell and looks useless."),
            Comment("mon-3", null, 5, "CMC is still great.")
        };

        if (includeViralOneOff)
        {
            mondayComments.Add(Comment(
                "mon-viral",
                null,
                10_000,
                "One Comment Wonder is elite, incredible, and the best player alive."));
        }

        var posts = new List<SnapshotSourcePost>
        {
            Post(
                "post-thu",
                "OFFICIAL THURSDAY NIGHT GAME THREAD",
                "2026-09-17T23:00:00Z",
                Comment("thu-1", null, 500, "Drake London is an elite monster league winner and smash start."),
                Comment("thu-2", null, 100, "Jefferson is cooked and unstartable in this offense."),
                Comment("thu-3", null, 20, "Tank Bigsby is a must add waiver steal.")),
            Post(
                "post-sun",
                "OFFICIAL SUNDAY GAME THREAD",
                "2026-09-20T17:00:00Z",
                Comment("sun-1", null, 250, "London looks great and locked in."),
                Comment("sun-2", null, 500, "Jefferson looks washed, awful, and terrible."),
                Comment("sun-3", "sun-2", 10, "CMC is automatic and elite.")),
            Post(
                "post-mon",
                "OFFICIAL MONDAY NIGHT GAME THREAD",
                "2026-09-22T00:15:00Z",
                mondayComments.ToArray())
        };

        return new SnapshotSourceData("2026-week-4", "2026 Week 4", 2026, 4, posts);
    }

    private static SnapshotSourceData ParentInheritanceSource()
    {
        return new SnapshotSourceData(
            "context-test",
            "Context Test",
            2026,
            4,
            [
                Post(
                    "post-context",
                    "OFFICIAL CONTEXT TEST THREAD",
                    "2026-09-20T17:00:00Z",
                    Comment("ctx-parent", null, 12, "Pitts got 2 targets."),
                    Comment("ctx-child", "ctx-parent", 8, "Dropping this dude immediately."))
            ]);
    }

    private static SnapshotSourcePost Post(
        string redditId,
        string title,
        string createdAt,
        params SnapshotSourceComment[] comments)
    {
        return new SnapshotSourcePost(
            redditId,
            title,
            DateTimeOffset.Parse(createdAt),
            100,
            comments);
    }

    private static SnapshotSourceComment Comment(
        string redditId,
        string? parentRedditId,
        int score,
        string body)
    {
        return new SnapshotSourceComment(
            redditId,
            parentRedditId,
            DateTimeOffset.UtcNow,
            score,
            body);
    }

    private static SentimentLexiconOptions Lexicon()
    {
        return new SentimentLexiconOptions
        {
            PositiveTerms = [ "elite", "monster", "league winner", "smash start", "great", "locked in", "must add", "waiver steal", "automatic", "stud", "breakout", "incredible" ],
            NegativeTerms = [ "cooked", "unstartable", "washed", "awful", "terrible", "panic", "sell", "useless" ],
            Intensifiers = [ "absolutely" ],
            Negators = [ "not", "never" ]
        };
    }

    private static IntentLexiconOptions Intents()
    {
        return new IntentLexiconOptions
        {
            Add = [ "add", "must add", "waiver" ],
            Drop = [ "drop" ],
            Start = [ "start", "smash start" ],
            Sit = [ "sit", "bench", "unstartable" ],
            Buy = [ "buy" ],
            Sell = [ "sell" ],
            Hold = [ "hold" ],
            Injury = [ "hurt", "injury" ],
            Hype = [ "league winner", "breakout", "monster" ],
            Panic = [ "panic" ]
        };
    }

    private sealed class StaticSnapshotSource(SnapshotSourceData source) : ISnapshotSource
    {
        public Task<SnapshotSourceData> LoadAsync(string key, CancellationToken cancellationToken)
        {
            return Task.FromResult(source);
        }
    }

    private sealed class CapturingSentimentAnalyzer : ISentimentAnalyzer
    {
        public List<TargetedSentimentRequest> Requests { get; } = [];

        public ValueTask<TargetedSentimentResult> AnalyzeAsync(
            TargetedSentimentRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new TargetedSentimentResult(
                -0.5,
                -0.5,
                SentimentCategories.Negative,
                -0.5,
                null,
                0,
                nameof(CapturingSentimentAnalyzer),
                request.Context));
        }
    }

    private sealed class FakeEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class SnapshotFixtureDirectory : IDisposable
    {
        private SnapshotFixtureDirectory(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static async Task<SnapshotFixtureDirectory> CreateAsync(string key, string json)
        {
            var root = Path.Combine(Path.GetTempPath(), $"fantasy-snapshot-tests-{Guid.NewGuid():N}");
            var directory = Path.Combine(root, "SeedData", "Snapshots");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, $"{key}.json"), json);
            return new SnapshotFixtureDirectory(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
