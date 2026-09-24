using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using FantasySentiment.Api.Providers;
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
    public async Task VaderSentiment_HandlesPositiveNegativeNegationAndIntensifiers()
    {
        var analyzer = VaderAnalyzer();
        var mention = TestMention();

        var positive = await analyzer.AnalyzeAsync(
            new TargetedSentimentRequest("CMC is very good.", "CMC is very good.", mention),
            CancellationToken.None);
        var negative = await analyzer.AnalyzeAsync(
            new TargetedSentimentRequest("CMC is very bad.", "CMC is very bad.", mention),
            CancellationToken.None);
        var negated = await analyzer.AnalyzeAsync(
            new TargetedSentimentRequest("CMC is not bad at all.", "CMC is not bad at all.", mention),
            CancellationToken.None);
        var basePositive = await analyzer.AnalyzeAsync(
            new TargetedSentimentRequest("CMC is good.", "CMC is good.", mention),
            CancellationToken.None);

        Assert.Equal(SentimentCategories.Positive, positive.SentimentCategory);
        Assert.Equal(SentimentCategories.Negative, negative.SentimentCategory);
        Assert.Equal(SentimentCategories.Positive, negated.SentimentCategory);
        Assert.True(positive.RawSentimentScore > basePositive.RawSentimentScore);
    }

    [Theory]
    [InlineData("CMC is absolutely disgusting.", "Christian McCaffrey", SentimentCategories.Positive)]
    [InlineData("Jefferson is washed.", "Justin Jefferson", SentimentCategories.Negative)]
    [InlineData("CMC is a league winner.", "Christian McCaffrey", SentimentCategories.Positive)]
    public async Task VaderSentiment_AppliesFantasyLanguageAdjustments(
        string text,
        string playerName,
        string expectedCategory)
    {
        var players = TestPlayers();
        var detector = new PlayerMentionDetector();
        var contextExtractor = new MentionContextExtractor();
        var analyzer = VaderAnalyzer();
        var match = detector.DetectMentions(text, players).Single(match => match.PlayerName == playerName);

        var result = await AnalyzeAsync(text, match, contextExtractor, analyzer);

        Assert.Equal(expectedCategory, result.SentimentCategory);
        Assert.NotNull(result.ModelScore);
        Assert.Contains("vader=", result.Diagnostics);
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

    [Fact]
    public async Task VaderTargetedSentiment_UsesMentionContextForMultiplePlayers()
    {
        var players = TestPlayers();
        var detector = new PlayerMentionDetector();
        var contextExtractor = new MentionContextExtractor();
        var analyzer = VaderAnalyzer();
        var text = "CMC looked great but Purdy was awful.";
        var matches = detector.DetectMentions(text, players);

        var cmc = await AnalyzeAsync(text, matches.Single(match => match.PlayerName == "Christian McCaffrey"), contextExtractor, analyzer);
        var purdy = await AnalyzeAsync(text, matches.Single(match => match.PlayerName == "Brock Purdy"), contextExtractor, analyzer);

        Assert.Equal(SentimentCategories.Positive, cmc.SentimentCategory);
        Assert.Equal(SentimentCategories.Negative, purdy.SentimentCategory);
    }

    [Fact]
    public void ThreadContextBuilder_IncludesThreadParentCommentAndTarget()
    {
        var builder = new ThreadContextBuilder();
        var context = builder.Build(
            "Baker Mayfield unlocks the rookie WR room",
            "Baker looked shaky early, so I'm not buying every pass catcher.",
            "Egbuka is a league winner if Baker keeps pushing volume.",
            "Egbuka is a league winner",
            "Emeka Egbuka");

        Assert.Contains("Thread title: Baker Mayfield", context);
        Assert.Contains("Parent: Baker looked shaky", context);
        Assert.Contains("Comment: Egbuka is a league winner", context);
        Assert.Contains("Target context: Egbuka is a league winner", context);
        Assert.Contains("Target: Emeka Egbuka", context);
    }

    [Fact]
    public async Task ThreadContextBuilder_AllowsTargetSpecificFantasyCorrection()
    {
        var builder = new ThreadContextBuilder();
        var analyzer = VaderAnalyzer();
        var mention = new PlayerMentionMatch(6, "Emeka Egbuka", "TB", "WR", "Egbuka", 0, 6);
        var context = builder.Build(
            "Baker Mayfield unlocks the rookie WR room",
            "Baker looked shaky early, so I'm not buying every pass catcher.",
            "Egbuka is a league winner if Baker keeps pushing volume.",
            "Egbuka is a league winner",
            "Emeka Egbuka");

        var result = await analyzer.AnalyzeAsync(
            new TargetedSentimentRequest("Egbuka is a league winner if Baker keeps pushing volume.", context, mention),
            CancellationToken.None);

        Assert.Equal(SentimentCategories.Positive, result.SentimentCategory);
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
    public void BuzzScore_UsesRecentRateAgainstBaseline()
    {
        var calculator = new BuzzScoreCalculator();
        var buzz = calculator.Calculate(15, TimeSpan.FromHours(1), 24, TimeSpan.FromHours(24));

        Assert.True(buzz > 10);
    }

    [Fact]
    public void RedditPostAnalysisDecider_UsesThresholdsAndPostTypes()
    {
        var now = DateTimeOffset.UtcNow;
        var decider = new RedditPostAnalysisDecider(Options.Create(new RedditAnalysisOptions()));

        var quiet = decider.Decide(Post("p1", "First look at sleeper rankings", now.AddMinutes(-20), 39, 20), now);
        var scored = decider.Decide(Post("p2", "Sleeper rankings", now.AddMinutes(-20), 40, 50), now);
        var normal = decider.Decide(Post("p3", "Waiver reactions", now.AddMinutes(-20), 100, 5), now);
        var news = decider.Decide(Post("p4", "Injury update: starter questionable", now.AddMinutes(-20), 30, 3), now);

        Assert.Equal(RedditAnalysisStatuses.Collecting, quiet.Status);
        Assert.Equal(RedditPostTypes.Normal, quiet.PostType);
        Assert.Equal(RedditAnalysisStatuses.ReadyForAnalysis, scored.Status);
        Assert.Equal(RedditAnalysisStatuses.ReadyForAnalysis, normal.Status);
        Assert.Equal(RedditPostTypes.ImportantNews, news.PostType);
        Assert.Equal(RedditAnalysisStatuses.ReadyForAnalysis, news.Status);
    }

    [Fact]
    public void RedditPostAnalysisDecider_WaitsForGameThreadCooldown()
    {
        var now = DateTimeOffset.UtcNow;
        var decider = new RedditPostAnalysisDecider(Options.Create(new RedditAnalysisOptions()));
        var active = Post("p1", "GAME THREAD: Sunday chaos", now.AddHours(-4), 6067, 230);
        active.PreviousCommentCount = 6040;
        active.LastObservedAt = now;
        active.LastObservedCommentCountAt = now.AddMinutes(-20);

        var decision = decider.Decide(active, now);

        Assert.Equal(RedditPostTypes.GameThread, decision.PostType);
        Assert.Equal(RedditAnalysisStatuses.Collecting, decision.Status);
        Assert.Contains("game still active", decision.Reason);

        active.PreviousCommentCount = 6061;
        decision = decider.Decide(active, now);

        Assert.Equal(RedditAnalysisStatuses.ReadyForAnalysis, decision.Status);
        Assert.Contains("game activity cooled", decision.Reason);
    }

    [Fact]
    public void RedditPostAnalysisDecider_UsesIncrementalThresholdAfterAnalysis()
    {
        var now = DateTimeOffset.UtcNow;
        var decider = new RedditPostAnalysisDecider(Options.Create(new RedditAnalysisOptions
        {
            IncrementalMinimumNewComments = 2
        }));
        var post = Post("p1", "Waiver reactions", now.AddHours(-1), 101, 20);
        post.LastAnalyzedAt = now.AddMinutes(-10);
        post.LastAnalyzedCommentCount = 100;

        var oneNewComment = decider.Decide(post, now);
        post.CommentCount = 102;
        var twoNewComments = decider.Decide(post, now);

        Assert.Equal(RedditAnalysisStatuses.Analyzed, oneNewComment.Status);
        Assert.Equal(RedditAnalysisStatuses.ReadyForAnalysis, twoNewComments.Status);
    }

    [Fact]
    public async Task RedditIngestion_AnalyzesOnlyNewOrChangedCommentsOnIncrementalRun()
    {
        var options = new DbContextOptionsBuilder<FantasySentimentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new FantasySentimentDbContext(options);
        db.Players.Add(new Player
        {
            Name = "Garrett Wilson",
            Team = "NYJ",
            Position = "WR",
            Aliases = [new PlayerAlias { Alias = "Garrett Wilson" }]
        });
        await db.SaveChangesAsync();

        var provider = new IncrementalRedditDataProvider();
        var service = CreateIngestionService(db, provider);

        await service.DiscoverPostsAsync(CancellationToken.None);
        var firstResult = await service.AnalyzeReadyPostsAsync(CancellationToken.None);

        provider.Phase = 2;
        await service.DiscoverPostsAsync(CancellationToken.None);
        var secondResult = await service.AnalyzeReadyPostsAsync(CancellationToken.None);

        var mentions = await db.PlayerMentions
            .Include(mention => mention.Comment)
            .OrderBy(mention => mention.Comment.RedditId)
            .ToListAsync();
        var post = await db.RedditPosts.SingleAsync();

        Assert.Equal(1, firstResult.PlayerMentions);
        Assert.Equal(1, secondResult.PlayerMentions);
        Assert.Equal(2, mentions.Count);
        Assert.Equal(20, mentions[0].RedditScore);
        Assert.Equal("incremental-post-c1", mentions[0].Comment.RedditId);
        Assert.Equal("incremental-post-c2", mentions[1].Comment.RedditId);
        Assert.Equal(RedditAnalysisStatuses.Analyzed, post.AnalysisStatus);
        Assert.Equal(2, post.LastAnalyzedCommentCount);
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
            },
            new Player
            {
                Id = 6,
                Name = "Emeka Egbuka",
                Team = "TB",
                Position = "WR",
                Aliases = [new PlayerAlias { Alias = "Egbuka" }]
            },
            new Player
            {
                Id = 7,
                Name = "Baker Mayfield",
                Team = "TB",
                Position = "QB",
                Aliases = [new PlayerAlias { Alias = "Baker" }]
            }
        ];
    }

    private static VaderSentimentAnalyzer VaderAnalyzer()
    {
        return new VaderSentimentAnalyzer(new FantasySentimentAnalyzer(new TestOptionsMonitor<SentimentLexiconOptions>(Lexicon())));
    }

    private static PlayerMentionMatch TestMention()
    {
        return new PlayerMentionMatch(1, "Christian McCaffrey", "SF", "RB", "CMC", 0, 3);
    }

    private static RedditPost Post(
        string redditId,
        string title,
        DateTimeOffset createdAt,
        int commentCount,
        int score)
    {
        return new RedditPost
        {
            RedditId = redditId,
            Subreddit = "fantasyfootball",
            Title = title,
            CreatedAt = createdAt,
            Url = $"https://reddit.example/{redditId}",
            CommentCount = commentCount,
            Score = score,
            LastObservedAt = DateTimeOffset.UtcNow,
            AnalysisStatus = RedditAnalysisStatuses.Discovered,
            PostType = RedditPostTypes.Normal
        };
    }

    private static RedditIngestionService CreateIngestionService(
        FantasySentimentDbContext db,
        IRedditDataProvider provider)
    {
        var analysisOptions = new RedditAnalysisOptions
        {
            MaxPostsPerAnalysisBatch = 5,
            IncrementalMinimumNewComments = 1,
            NormalPosts = new NormalPostAnalysisOptions
            {
                MinimumComments = 1,
                MinimumCommentsWithScore = 1,
                MinimumPostScore = 0
            }
        };

        return new RedditIngestionService(
            db,
            provider,
            new RedditPostAnalysisDecider(Options.Create(analysisOptions)),
            new PlayerMentionDetector(),
            VaderAnalyzer(),
            new MentionContextExtractor(),
            new ThreadContextBuilder(),
            new FantasyIntentClassifier(new TestOptionsMonitor<IntentLexiconOptions>(Intents())),
            new VoteWeightCalculator(),
            new SnapshotAggregationService(
                db,
                new BuzzScoreCalculator(),
                Options.Create(new SnapshotOptions { IntervalMinutes = 5, RecentWindowMinutes = 60, BaselineWindowHours = 24 }),
                NullLogger<SnapshotAggregationService>.Instance),
            Options.Create(new RedditOptions
            {
                UseMockReddit = true,
                Subreddits = ["fantasyfootball"],
                FetchLookbackMinutes = 240
            }),
            Options.Create(analysisOptions),
            NullLogger<RedditIngestionService>.Instance);
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

    private sealed class IncrementalRedditDataProvider : IRedditDataProvider
    {
        private readonly DateTimeOffset _createdAt = DateTimeOffset.UtcNow.AddMinutes(-30);

        public int Phase { get; set; } = 1;

        public Task<IReadOnlyList<RedditPostData>> DiscoverRecentPostsAsync(
            string subreddit,
            DateTimeOffset since,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<RedditPostData> posts =
            [
                new RedditPostData(
                    "incremental-post",
                    subreddit,
                    "Garrett Wilson start sit reactions",
                    _createdAt,
                    "https://reddit.example/incremental-post",
                    100,
                    Phase >= 2 ? 2 : 1)
            ];

            return Task.FromResult(posts);
        }

        public Task<IReadOnlyList<RedditCommentData>> FetchCommentsAsync(
            string subreddit,
            string postRedditId,
            DateTimeOffset postCreatedAt,
            CancellationToken cancellationToken)
        {
            var comments = new List<RedditCommentData>
            {
                new(
                    "incremental-post-c1",
                    null,
                    "Garrett Wilson usage is still elite.",
                    postCreatedAt.AddMinutes(5),
                    Phase >= 2 ? 20 : 5)
            };

            if (Phase >= 2)
            {
                comments.Add(new RedditCommentData(
                    "incremental-post-c2",
                    "incremental-post-c1",
                    "Garrett Wilson looks washed if this offense keeps stalling.",
                    postCreatedAt.AddMinutes(10),
                    3));
            }

            return Task.FromResult<IReadOnlyList<RedditCommentData>>(comments);
        }
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
