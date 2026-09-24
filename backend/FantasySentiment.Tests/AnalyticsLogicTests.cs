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
    public async Task TargetedSentiment_UsesMentionContextForMultiplePlayers()
    {
        var players = TestPlayers();
        var detector = new PlayerMentionDetector();
        var contextExtractor = new MentionContextExtractor();
        var analyzer = new RuleBasedFantasySentimentAnalyzer(
            new FantasySentimentAnalyzer(new TestOptionsMonitor<SentimentLexiconOptions>(Lexicon())));
        var text = "CMC looked great but Purdy was awful.";
        var matches = detector.DetectMentions(text, players);

        var cmc = await AnalyzeAsync(text, matches.Single(match => match.PlayerName == "Christian McCaffrey"), contextExtractor, analyzer);
        var purdy = await AnalyzeAsync(text, matches.Single(match => match.PlayerName == "Brock Purdy"), contextExtractor, analyzer);

        Assert.Equal(SentimentCategories.Positive, cmc.SentimentCategory);
        Assert.Equal(SentimentCategories.Negative, purdy.SentimentCategory);
    }

    [Theory]
    [InlineData("CMC is absolutely disgusting.", "Christian McCaffrey", SentimentCategories.Positive)]
    [InlineData("Jefferson is washed.", "Justin Jefferson", SentimentCategories.Negative)]
    [InlineData("Thanks for the 2 points Adams.", "Davante Adams", SentimentCategories.Negative)]
    [InlineData("Adams I hope both sides of your pillow are cool as fuck tonight.", "Davante Adams", SentimentCategories.Positive)]
    public async Task TargetedSentiment_HandlesFantasyLanguage(
        string text,
        string playerName,
        string expectedCategory)
    {
        var players = TestPlayers();
        var detector = new PlayerMentionDetector();
        var contextExtractor = new MentionContextExtractor();
        var analyzer = new RuleBasedFantasySentimentAnalyzer(
            new FantasySentimentAnalyzer(new TestOptionsMonitor<SentimentLexiconOptions>(Lexicon())));
        var match = detector.DetectMentions(text, players).Single(match => match.PlayerName == playerName);

        var result = await AnalyzeAsync(text, match, contextExtractor, analyzer);

        Assert.Equal(expectedCategory, result.SentimentCategory);
    }

    [Fact]
    public async Task SentimentAndIntent_RemainSeparateSignals()
    {
        var players = TestPlayers();
        var detector = new PlayerMentionDetector();
        var contextExtractor = new MentionContextExtractor();
        var sentimentAnalyzer = new RuleBasedFantasySentimentAnalyzer(
            new FantasySentimentAnalyzer(new TestOptionsMonitor<SentimentLexiconOptions>(Lexicon())));
        var intentClassifier = new FantasyIntentClassifier(new TestOptionsMonitor<IntentLexiconOptions>(Intents()));
        var sellText = "I love Breece but I'm selling high.";
        var sellMatch = detector.DetectMentions(sellText, players).Single();

        var sellSentiment = await AnalyzeAsync(sellText, sellMatch, contextExtractor, sentimentAnalyzer);

        Assert.Equal(SentimentCategories.Positive, sellSentiment.SentimentCategory);
        Assert.Equal(FantasyIntents.Sell, intentClassifier.Classify(sellText, sellMatch));

        var injuryText = "Is Jefferson hurt?";
        var injuryMatch = detector.DetectMentions(injuryText, players).Single();
        var injurySentiment = await AnalyzeAsync(injuryText, injuryMatch, contextExtractor, sentimentAnalyzer);

        Assert.Equal(SentimentCategories.Neutral, injurySentiment.SentimentCategory);
        Assert.Equal(FantasyIntents.Injury, intentClassifier.Classify(injuryText, injuryMatch));
    }

    [Fact]
    public void VoteWeight_UsesBoundedDiminishingReturns()
    {
        var calculator = new VoteWeightCalculator();

        var zero = calculator.Calculate(0);
        var one = calculator.Calculate(1);
        var ten = calculator.Calculate(10);
        var hundred = calculator.Calculate(100);
        var thousand = calculator.Calculate(1000);
        var negative = calculator.Calculate(-20);
        var extreme = calculator.Calculate(1_000_000);

        Assert.Equal(1, zero);
        Assert.True(one > zero);
        Assert.True(ten > one);
        Assert.True(hundred > ten);
        Assert.True(thousand > hundred);
        Assert.True(thousand < hundred * 2);
        Assert.InRange(negative, 0.25, 1);
        Assert.Equal(4, extreme);
    }

    [Fact]
    public void RobertaScoreMapper_UsesExpectedValueAcrossFiveProbabilities()
    {
        var mapped = RobertaSentimentScoreMapper.Map([0, 0, 0, 0, 4]);

        Assert.Equal("strongly positive", mapped.WinningLabel);
        Assert.True(mapped.Score > 0.8);
        Assert.Equal(5, mapped.Probabilities.Count);
    }

    [Fact]
    public void RobertaRuntimeStatus_ReportsMissingRequiredAssets()
    {
        var assets = new RobertaAssetPaths(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "model.onnx"),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "tokenizer.json"),
            "config.json",
            "tokenizer_config.json");

        var status = RobertaRuntimeStatus.MissingAssets(assets, assets.MissingRequiredAssets().ToArray());

        Assert.True(status.Enabled);
        Assert.False(status.Loaded);
        Assert.False(status.ModelExists);
        Assert.False(status.TokenizerExists);
        Assert.Contains("model.onnx", status.ErrorMessage);
        Assert.Contains("tokenizer.json", status.ErrorMessage);
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
            RawSentimentScore = 0.7,
            FinalSentimentScore = 0.7,
            SentimentScore = 0.7,
            RuleBasedSentimentScore = 0.7,
            SentimentCategory = SentimentCategories.Positive,
            SentimentAnalyzer = nameof(RuleBasedFantasySentimentAnalyzer),
            SentimentContext = "Puka is elite",
            MatchedAlias = "Puka",
            Intent = FantasyIntents.Hype,
            RedditScore = 8,
            VoteWeight = 1.879,
            WeightedSentimentContribution = 1.315
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
        Assert.Equal(0.7, snapshot.SentimentScore);
        Assert.Equal(0.7, snapshot.CommunitySentimentScore);
    }

    [Fact]
    public async Task SnapshotAggregation_UsesVoteWeightsForCommunitySentiment()
    {
        var options = new DbContextOptionsBuilder<FantasySentimentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new FantasySentimentDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var player = new Player { Name = "Davante Adams", Team = "LAR", Position = "WR" };
        var post = new RedditPost
        {
            RedditId = "p1",
            Subreddit = "fantasyfootball",
            Title = "thread",
            CreatedAt = now,
            CommentCount = 2
        };
        var positiveComment = new RedditComment
        {
            RedditId = "c1",
            Post = post,
            Body = "Adams is great",
            CreatedAt = now.AddMinutes(-10),
            Score = 1
        };
        var negativeComment = new RedditComment
        {
            RedditId = "c2",
            Post = post,
            Body = "Adams was awful",
            CreatedAt = now.AddMinutes(-8),
            Score = 1000
        };

        db.Add(player);
        db.Add(post);
        db.AddRange(positiveComment, negativeComment);
        await db.SaveChangesAsync();

        var voteWeightCalculator = new VoteWeightCalculator();
        AddMention(db, player.Id, positiveComment, 0.8, voteWeightCalculator.Calculate(positiveComment.Score));
        AddMention(db, player.Id, negativeComment, -0.8, voteWeightCalculator.Calculate(negativeComment.Score));
        await db.SaveChangesAsync();

        var service = new SnapshotAggregationService(
            db,
            new BuzzScoreCalculator(),
            Options.Create(new SnapshotOptions { IntervalMinutes = 5, RecentWindowMinutes = 60, BaselineWindowHours = 24 }),
            NullLogger<SnapshotAggregationService>.Instance);

        await service.GenerateSnapshotsAsync(now, CancellationToken.None);

        var snapshot = await db.PlayerSnapshots.SingleAsync();
        Assert.Equal(0, snapshot.SentimentScore);
        Assert.InRange(snapshot.CommunitySentimentScore, -0.8, 0);
        Assert.True(snapshot.TotalVoteWeight < 6);
        Assert.Equal("Low", snapshot.Confidence);
    }

    private static SentimentLexiconOptions Lexicon()
    {
        return new SentimentLexiconOptions
        {
            PositiveTerms = [ "elite", "league winner", "beast", "great", "love", "disgusting", "cool as fuck", "pillow are cool" ],
            NegativeTerms = [ "washed", "unstartable", "useless", "awful", "2 points", "thanks for the 2 points" ],
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
            Sell = [ "sell", "selling", "sell high" ],
            Hold = [ "hold" ],
            Injury = [ "hurt" ],
            Hype = [ "league winner" ],
            Panic = [ "panic" ]
        };
    }

    private static IReadOnlyList<Player> TestPlayers()
    {
        return
        [
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
                Name = "Brock Purdy",
                Team = "SF",
                Position = "QB",
                Aliases = [new PlayerAlias { Alias = "Purdy" }]
            },
            new Player
            {
                Id = 3,
                Name = "Justin Jefferson",
                Team = "MIN",
                Position = "WR",
                Aliases = [new PlayerAlias { Alias = "Jefferson" }]
            },
            new Player
            {
                Id = 4,
                Name = "Davante Adams",
                Team = "LAR",
                Position = "WR",
                Aliases = [new PlayerAlias { Alias = "Adams" }]
            },
            new Player
            {
                Id = 5,
                Name = "Breece Hall",
                Team = "NYJ",
                Position = "RB",
                Aliases = [new PlayerAlias { Alias = "Breece" }]
            }
        ];
    }

    private static ValueTask<TargetedSentimentResult> AnalyzeAsync(
        string text,
        PlayerMentionMatch match,
        MentionContextExtractor contextExtractor,
        ISentimentAnalyzer analyzer)
    {
        var context = contextExtractor.ExtractContext(text, match);
        return analyzer.AnalyzeAsync(new TargetedSentimentRequest(text, context, match), CancellationToken.None);
    }

    private static void AddMention(
        FantasySentimentDbContext db,
        int playerId,
        RedditComment comment,
        double sentiment,
        double voteWeight)
    {
        db.PlayerMentions.Add(new PlayerMention
        {
            PlayerId = playerId,
            CommentId = comment.Id,
            CreatedAt = comment.CreatedAt,
            RawSentimentScore = sentiment,
            FinalSentimentScore = sentiment,
            SentimentScore = sentiment,
            RuleBasedSentimentScore = sentiment,
            SentimentCategory = TargetedSentimentResult.CategoryFromScore(sentiment),
            SentimentAnalyzer = nameof(RuleBasedFantasySentimentAnalyzer),
            SentimentContext = comment.Body,
            MatchedAlias = "Adams",
            Intent = FantasyIntents.General,
            RedditScore = comment.Score,
            VoteWeight = voteWeight,
            WeightedSentimentContribution = Math.Round(sentiment * voteWeight, 3)
        });
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
