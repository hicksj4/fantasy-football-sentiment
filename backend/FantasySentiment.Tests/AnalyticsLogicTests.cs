using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using FantasySentiment.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Tests;

public sealed class AnalyticsLogicTests
{
    [Fact]
    public void MentionDetector_MatchesAliasesAndSkipsAmbiguousSingleNames()
    {
        var detector = new PlayerMentionDetector();
        var players = new[]
        {
            new Player
            {
                Id = 1,
                Name = "Christian McCaffrey",
                Team = "SF",
                Position = "RB",
                Aliases = [new PlayerAlias { Alias = "CMC" }]
            },
            new Player
            {
                Id = 2,
                Name = "Javonte Williams",
                Team = "DAL",
                Position = "RB",
                Aliases = [new PlayerAlias { Alias = "Williams" }]
            }
        };

        var cmcMatches = detector.DetectMentions("CMC looks absolutely insane today.", players);
        var ambiguousMatches = detector.DetectMentions("Williams is a start this week.", players);

        Assert.Single(cmcMatches);
        Assert.Equal("Christian McCaffrey", cmcMatches[0].PlayerName);
        Assert.Empty(ambiguousMatches);
    }

    [Fact]
    public void SentimentAnalyzer_HandlesPositiveNegativeAndNegators()
    {
        var analyzer = new FantasySentimentAnalyzer(new TestOptionsMonitor<SentimentLexiconOptions>(Lexicon()));

        var positive = analyzer.Analyze("Puka is absolutely elite. League winner.");
        var negative = analyzer.Analyze("Pitts is washed and unstartable.");
        var negated = analyzer.Analyze("Breece is not useless.");

        Assert.Equal(SentimentCategories.Positive, positive.Category);
        Assert.Equal(SentimentCategories.Negative, negative.Category);
        Assert.Equal(SentimentCategories.Positive, negated.Category);
    }

    [Fact]
    public void IntentClassifier_ClassifiesDropForBothSides()
    {
        var classifier = new FantasyIntentClassifier(new TestOptionsMonitor<IntentLexiconOptions>(Intents()));
        var andrew = new PlayerMentionMatch(1, "Mark Andrews", "BAL", "TE", "Andrews", 14, 7);
        var likely = new PlayerMentionMatch(2, "Isaiah Likely", "BAL", "TE", "Likely", 26, 6);

        var text = "Should I drop Andrews for Likely?";

        Assert.Equal(FantasyIntents.Drop, classifier.Classify(text, andrew));
        Assert.Equal(FantasyIntents.Add, classifier.Classify(text, likely));
    }

    [Fact]
    public void BuzzScore_UsesRecentRateAgainstBaseline()
    {
        var calculator = new BuzzScoreCalculator();
        var buzz = calculator.Calculate(15, TimeSpan.FromHours(1), 24, TimeSpan.FromHours(24));

        Assert.True(buzz > 10);
    }

    [Fact]
    public async Task SnapshotAggregation_CreatesPlayerSnapshotFromMentions()
    {
        var options = new DbContextOptionsBuilder<FantasySentimentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new FantasySentimentDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var player = new Player { Name = "Puka Nacua", Team = "LAR", Position = "WR" };
        var post = new RedditPost
        {
            RedditId = "p1",
            Subreddit = "fantasyfootball",
            Title = "thread",
            CreatedAt = now,
            CommentCount = 2
        };
        var comment = new RedditComment
        {
            RedditId = "c1",
            Post = post,
            Body = "Puka is elite",
            CreatedAt = now.AddMinutes(-10),
            Score = 8
        };

        db.Add(player);
        db.Add(post);
        db.Add(comment);
        await db.SaveChangesAsync();

        db.PlayerMentions.Add(new PlayerMention
        {
            PlayerId = player.Id,
            CommentId = comment.Id,
            CreatedAt = comment.CreatedAt,
            SentimentScore = 0.7,
            SentimentCategory = SentimentCategories.Positive,
            Intent = FantasyIntents.Hype
        });
        await db.SaveChangesAsync();

        var service = new SnapshotAggregationService(
            db,
            new BuzzScoreCalculator(),
            Options.Create(new SnapshotOptions { IntervalMinutes = 5, RecentWindowMinutes = 60, BaselineWindowHours = 24 }),
            NullLogger<SnapshotAggregationService>.Instance);

        await service.GenerateSnapshotsAsync(now, CancellationToken.None);

        var snapshot = await db.PlayerSnapshots.SingleAsync();
        Assert.Equal(1, snapshot.MentionCount);
        Assert.Equal(1, snapshot.PositiveCount);
        Assert.Equal(1, snapshot.HypeCount);
    }

    private static SentimentLexiconOptions Lexicon()
    {
        return new SentimentLexiconOptions
        {
            PositiveTerms = [ "elite", "league winner", "beast" ],
            NegativeTerms = [ "washed", "unstartable", "useless" ],
            Intensifiers = [ "absolutely" ],
            Negators = [ "not", "never" ]
        };
    }

    private static IntentLexiconOptions Intents()
    {
        return new IntentLexiconOptions
        {
            Add = [ "add", "waiver" ],
            Drop = [ "drop" ],
            Start = [ "start" ],
            Sit = [ "sit", "bench" ],
            Buy = [ "buy" ],
            Sell = [ "sell" ],
            Hold = [ "hold" ],
            Injury = [ "hurt" ],
            Hype = [ "league winner" ],
            Panic = [ "panic" ]
        };
    }
}

internal sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name)
    {
        return value;
    }

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        return null;
    }
}
