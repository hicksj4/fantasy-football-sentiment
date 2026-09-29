using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using FantasySentiment.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Tests;

public sealed class ScoreClarityTests
{
    [Fact]
    public void VoteWeighting_DiagnosticCurve_UsesExpectedShape()
    {
        var calculator = new VoteWeightCalculator();
        var scores = new[] { -100, -20, -10, -6, -3, -1, 0, 1, 2, 5, 10, 50, 100, 500, 1000, 5000 };
        var weights = scores.ToDictionary(score => score, calculator.Calculate);

        foreach (var score in scores)
        {
            Console.WriteLine($"{score,5}: {weights[score]:0.###}");
        }

        Assert.Equal(1, weights[0]);
        Assert.Equal(1, weights[1]);
        Assert.InRange(weights[500], 4.0, 4.2);
        Assert.True(weights[500] > weights[10]);
        Assert.True(weights[500] < 6);
        Assert.InRange(weights[-6], 0.35, 0.40);
        Assert.Equal(0.1, weights[-100]);

        Assert.True(weights[2] > weights[1]);
        Assert.True(weights[5] > weights[2]);
        Assert.True(weights[10] > weights[5]);
        Assert.True(weights[50] > weights[10]);
        Assert.True(weights[100] > weights[50]);
        Assert.True(weights[500] > weights[100]);
        Assert.True(weights[1000] > weights[500]);
        Assert.True(weights[5000] > weights[1000]);
        Assert.True(weights[1000] - weights[500] < weights[100] - weights[50]);

        Assert.True(weights[-1] < weights[0]);
        Assert.True(weights[-3] < weights[-1]);
        Assert.True(weights[-6] < weights[-3]);
        Assert.True(weights[-10] < weights[-6]);
        Assert.True(weights[-20] < weights[-10]);
        Assert.True(weights[-100] <= weights[-20]);
    }

    [Fact]
    public void CommunitySentiment_UsesSuppressedRejectedCommentsWithoutInvertingThem()
    {
        var calculator = new VoteWeightCalculator();
        var rejectedNegative = new WeightedMention(-0.9, calculator.Calculate(-8));
        var endorsedPositive = new WeightedMention(0.4, calculator.Calculate(20));
        var unweightedSentiment = Math.Round((rejectedNegative.Sentiment + endorsedPositive.Sentiment) / 2, 3);
        var community = Community([rejectedNegative, endorsedPositive]);

        Assert.True(rejectedNegative.Sentiment < 0);
        Assert.True(rejectedNegative.VoteWeight < 0.35);
        Assert.True(community > unweightedSentiment);
        Assert.True(community > 0);
    }

    [Fact]
    public void VotingEffect_IsCommunityMinusUnweightedSentiment()
    {
        var mentions = new[]
        {
            new WeightedMention(0.8, 4),
            new WeightedMention(-0.2, 1)
        };
        var sentiment = mentions.Average(mention => mention.Sentiment);
        var community = Community(mentions);
        var votingEffect = Math.Round(community - sentiment, 3);

        Assert.Equal(Math.Round(community - sentiment, 3), votingEffect);
        Assert.True(votingEffect > 0);
    }

    [Fact]
    public void DiscussionImpact_IsNonDirectionalAndKeepsPositiveNegativeBreakdown()
    {
        var mentions = new[]
        {
            new WeightedMention(0.5, 3),
            new WeightedMention(-0.4, 2),
            new WeightedMention(0.2, 1)
        };

        var positiveImpact = mentions.Where(mention => mention.Sentiment > 0).Sum(mention => mention.Sentiment * mention.VoteWeight);
        var negativeImpact = mentions.Where(mention => mention.Sentiment < 0).Sum(mention => mention.Sentiment * mention.VoteWeight);
        var discussionImpact = Math.Abs(positiveImpact) + Math.Abs(negativeImpact);

        Assert.Equal(1.7, Math.Round(positiveImpact, 3));
        Assert.Equal(-0.8, Math.Round(negativeImpact, 3));
        Assert.Equal(2.5, Math.Round(discussionImpact, 3));
        Assert.True(discussionImpact > positiveImpact);
    }

    [Fact]
    public void CommunitySentiment_CanFavorHighlyEndorsedPositiveTakeOverSeveralLowScoreTakes()
    {
        var calculator = new VoteWeightCalculator();
        var mentions = new[]
        {
            new WeightedMention(0.8, calculator.Calculate(500)),
            new WeightedMention(-0.4, calculator.Calculate(1)),
            new WeightedMention(-0.4, calculator.Calculate(1)),
            new WeightedMention(-0.4, calculator.Calculate(1))
        };
        var sentiment = mentions.Average(mention => mention.Sentiment);
        var community = Community(mentions);

        Assert.True(sentiment < 0);
        Assert.True(community > 0);
    }

    [Fact]
    public void SentimentContributions_SumApproximatelyToCommunitySentiment()
    {
        var mentions = new[]
        {
            new WeightedMention(0.8, 4),
            new WeightedMention(-0.4, 2),
            new WeightedMention(0.2, 1)
        };
        var totalWeight = mentions.Sum(mention => mention.VoteWeight);
        var contributions = mentions
            .Select(mention => mention.Sentiment * mention.VoteWeight / totalWeight)
            .ToList();

        Assert.Equal(Community(mentions), Math.Round(contributions.Sum(), 3));
        Assert.Equal(0.457, Math.Round(contributions[0], 3));
    }

    [Fact]
    public void SentimentScoringPolicy_GatesLowSemanticConfidence()
    {
        var policy = new SentimentScoringPolicy();

        var lowSemantic = policy.Evaluate(0.40, 1);
        var productionScored = policy.Evaluate(0.45, 0.70);
        var strictDiagnosticScored = policy.Evaluate(SentimentScoringPolicy.StrictDiagnosticSemanticConfidence, 0.90);
        var weakTarget = policy.Evaluate(0.85, 0.40);

        Assert.Equal(AnalysisDispositions.MentionOnly, lowSemantic.Disposition);
        Assert.Equal(AnalysisDispositionReasons.LowSemanticConfidence, lowSemantic.Reason);
        Assert.Equal(AnalysisDispositions.Scored, productionScored.Disposition);
        Assert.Equal(AnalysisDispositions.Scored, strictDiagnosticScored.Disposition);
        Assert.Equal(AnalysisDispositionReasons.WeakTargetAttribution, weakTarget.Reason);
    }

    [Fact]
    public void VoteWeighting_OneViralCommentDoesNotCompletelyDominateSentiment()
    {
        var calculator = new VoteWeightCalculator();
        var viralPositive = new WeightedMention(0.9, calculator.Calculate(5000));
        var steadyNegative = Enumerable.Repeat(new WeightedMention(-0.6, calculator.Calculate(100)), 8);
        var community = Community(steadyNegative.Prepend(viralPositive));

        Assert.True(calculator.Calculate(5000) < calculator.Calculate(100) * 4);
        Assert.True(community < 0);
    }

    [Fact]
    public async Task PlayerExplanation_SelectsInfluentialEndorsedRejectedCommentsAndImpactRank()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var snapshot = new AnalysisSnapshot
        {
            Key = "score-test",
            Name = "Score Test",
            Season = 2026,
            Week = 4,
            CreatedAt = now,
            Status = AnalysisSnapshotStatuses.Processed
        };
        var alpha = Player("Bijan Robinson", "ATL", "RB", "Bijan");
        var beta = Player("Drake London", "ATL", "WR", "London");
        var gamma = Player("Kyle Pitts", "ATL", "TE", "Pitts");
        var post = new RedditPost
        {
            Snapshot = snapshot,
            RedditId = "post-1",
            Subreddit = "fantasyfootball",
            Title = "Game thread",
            CreatedAt = now,
            CommentCount = 3
        };
        db.AddRange(snapshot, alpha, beta, gamma, post);
        await db.SaveChangesAsync();

        var calculator = new VoteWeightCalculator();
        AddMention(db, snapshot, post, alpha, "c1", "Bijan is unstoppable tonight.", 500, 0.8, calculator.Calculate(500), TargetAttributionMethods.Explicit, 1);
        AddMention(db, snapshot, post, alpha, "c2", "Bijan is fine, not amazing.", 1, -0.2, calculator.Calculate(1), TargetAttributionMethods.Explicit, 1);
        AddMention(db, snapshot, post, alpha, "c3", "Bijan is washed.", -6, -0.9, calculator.Calculate(-6), TargetAttributionMethods.ParentInherited, 0.9);
        AddAggregate(db, snapshot, alpha, impactScore: 5.0);
        AddAggregate(db, snapshot, beta, impactScore: 10.0);
        AddAggregate(db, snapshot, gamma, impactScore: 1.0);
        await db.SaveChangesAsync();

        var query = new SnapshotQueryService(
            db,
            Options.Create(new DashboardHighlightOptions { CommentExcerptLength = 190 }),
            Options.Create(new ScoreExplanationOptions { MostRejectedMaximumRedditScore = -3, CommentExcerptLength = 190 }));

        var player = await query.GetPlayerAsync("score-test", alpha.Id, CancellationToken.None);

        Assert.NotNull(player);
        Assert.Equal(Math.Round(player.CommunitySentiment - player.Sentiment, 3), player.VotingEffect);
        Assert.Equal(5.0, player.DiscussionImpact);
        Assert.Equal(2, player.DiscussionImpactRank);
        Assert.Equal(50, player.DiscussionImpactPercentile);
        Assert.True(player.PositiveImpact > 0);
        Assert.True(player.NegativeImpact < 0);
        Assert.Contains("unstoppable", player.MostInfluentialComment?.CommentExcerpt);
        Assert.Contains("unstoppable", player.MostEndorsedComment?.CommentExcerpt);
        Assert.Contains("washed", player.MostRejectedComment?.CommentExcerpt);
        Assert.Equal(-6, player.MostRejectedComment?.RedditScore);
        Assert.Equal(-0.9, player.MostRejectedComment?.FinalSentimentScore);
        Assert.InRange(player.MostRejectedComment?.VoteWeight ?? 1, 0.35, 0.40);
        Assert.Equal(TargetAttributionMethods.ParentInherited, player.MostRejectedComment?.TargetAttributionMethod);
        Assert.Equal(0.9, player.MostRejectedComment?.TargetConfidence);
    }

    [Fact]
    public async Task PlayerExplanation_ExcludesMentionOnlyCommentsFromScoresAndInfluence()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var snapshot = new AnalysisSnapshot
        {
            Key = "mention-only-score-test",
            Name = "Mention Only Score Test",
            Season = 2026,
            Week = 4,
            CreatedAt = now,
            Status = AnalysisSnapshotStatuses.Processed
        };
        var player = Player("Tucker Kraft", "GB", "TE", "Kraft");
        var post = new RedditPost
        {
            Snapshot = snapshot,
            RedditId = "post-mention-only",
            Subreddit = "fantasyfootball",
            Title = "Game thread",
            CreatedAt = now,
            CommentCount = 2
        };
        db.AddRange(snapshot, player, post);
        await db.SaveChangesAsync();

        AddMention(db, snapshot, post, player, "scored", "Kraft is locked in.", 20, 0.6, 2.5, TargetAttributionMethods.Explicit, 1);
        AddMention(
            db,
            snapshot,
            post,
            player,
            "mention-only",
            "Kraft invisible.",
            5000,
            -0.9,
            9.5,
            TargetAttributionMethods.Explicit,
            1,
            semanticConfidence: 0.20,
            analysisDisposition: AnalysisDispositions.MentionOnly,
            analysisDispositionReason: AnalysisDispositionReasons.LowSemanticConfidence,
            fantasyActions: FantasyActions.Drop);
        AddAggregate(db, snapshot, player, impactScore: 1.5);
        await db.SaveChangesAsync();

        var query = new SnapshotQueryService(
            db,
            Options.Create(new DashboardHighlightOptions { CommentExcerptLength = 190 }),
            Options.Create(new ScoreExplanationOptions { CommentExcerptLength = 190 }));

        var metrics = await query.GetPlayerAsync("mention-only-score-test", player.Id, CancellationToken.None);

        Assert.NotNull(metrics);
        Assert.Equal(2, metrics.Mentions);
        Assert.Equal(1, metrics.ScoredMentionCount);
        Assert.Equal(1, metrics.MentionOnlyCount);
        Assert.Equal(0.5, metrics.SentimentCoverage);
        Assert.True(metrics.HasScoredSentiment);
        Assert.Equal(0.6, metrics.CommunitySentiment);
        Assert.Equal(1, metrics.DropCount);
        Assert.Contains("locked in", metrics.MostInfluentialComment?.CommentExcerpt);
        Assert.Contains("locked in", metrics.MostEndorsedComment?.CommentExcerpt);
    }

    [Fact]
    public async Task DashboardHighlights_RequireScoredSentimentEvidence()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var snapshot = new AnalysisSnapshot
        {
            Key = "mention-only-highlight-test",
            Name = "Mention Only Highlight Test",
            Season = 2026,
            Week = 4,
            CreatedAt = now,
            Status = AnalysisSnapshotStatuses.Processed
        };
        var player = Player("Noise Player", "FA", "WR", "Noise");
        var post = new RedditPost
        {
            Snapshot = snapshot,
            RedditId = "post-highlight",
            Subreddit = "fantasyfootball",
            Title = "Game thread",
            CreatedAt = now,
            CommentCount = 1
        };
        db.AddRange(snapshot, player, post);
        await db.SaveChangesAsync();

        AddMention(
            db,
            snapshot,
            post,
            player,
            "noise",
            "Noise Player is apparently amazing.",
            5000,
            0.95,
            9.5,
            TargetAttributionMethods.Explicit,
            1,
            semanticConfidence: 0.20,
            analysisDisposition: AnalysisDispositions.MentionOnly,
            analysisDispositionReason: AnalysisDispositionReasons.LowSemanticConfidence);
        AddAggregate(db, snapshot, player, impactScore: 0);
        await db.SaveChangesAsync();

        var query = new SnapshotQueryService(
            db,
            Options.Create(new DashboardHighlightOptions
            {
                MinimumMentions = 1,
                MinimumSampleStrength = 0,
                ExcludeLowConfidence = false,
                MinimumAbsoluteSentimentChange = 0
            }));

        var highlights = await query.GetHighlightsAsync("mention-only-highlight-test", CancellationToken.None);
        var row = Assert.Single(await query.GetPlayersAsync("mention-only-highlight-test", CancellationToken.None));

        Assert.Null(highlights?.Positive);
        Assert.Null(highlights?.Negative);
        Assert.False(row.HasScoredSentiment);
        Assert.Equal(0, row.CommunitySentiment);
        Assert.Equal(1, row.MentionOnlyCount);
    }

    [Fact]
    public async Task RollingHighlights_CanChangeWhenWindowChanges()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var snapshot = new AnalysisSnapshot
        {
            Key = "rolling-window-test",
            Name = "Rolling Window Test",
            Season = 2026,
            Week = 4,
            CreatedAt = now,
            Status = AnalysisSnapshotStatuses.Processed
        };
        var fast = Player("Fast Riser", "FA", "RB", "Fast");
        var slow = Player("Slow Burner", "FA", "WR", "Slow");
        var post = new RedditPost
        {
            Snapshot = snapshot,
            RedditId = "post-rolling",
            Subreddit = "fantasyfootball",
            Title = "Game thread",
            CreatedAt = now.AddDays(-2),
            CommentCount = 3
        };
        db.AddRange(snapshot, fast, slow, post);
        await db.SaveChangesAsync();

        AddMention(db, snapshot, post, fast, "fast-now", "Fast Riser looks great.", 10, 0.7, 2, TargetAttributionMethods.Explicit, 1, createdAt: now.AddMinutes(-30));
        AddMention(db, snapshot, post, slow, "slow-old-1", "Slow Burner is elite.", 50, 0.8, 4, TargetAttributionMethods.Explicit, 1, createdAt: now.AddDays(-2));
        AddMention(db, snapshot, post, slow, "slow-old-2", "Slow Burner league winner.", 50, 0.8, 4, TargetAttributionMethods.Explicit, 1, createdAt: now.AddDays(-2).AddMinutes(1));
        await db.SaveChangesAsync();

        var query = new AnalyticsQueryService(
            db,
            Options.Create(new DashboardHighlightOptions
            {
                MinimumMentions = 1,
                MinimumSampleStrength = 0,
                ExcludeLowConfidence = false,
                MinimumAbsoluteSentimentChange = 0,
                CommentExcerptLength = 190
            }),
            new SentimentScoringPolicy());

        var oneHour = await query.GetHighlightsAsync(TimeSpan.FromHours(1), CancellationToken.None);
        var sevenDays = await query.GetHighlightsAsync(TimeSpan.FromDays(7), CancellationToken.None);

        Assert.Equal("Fast Riser", oneHour.Positive?.Name);
        Assert.Equal("Slow Burner", sevenDays.Positive?.Name);
    }

    [Fact]
    public async Task DashboardHighlights_UseSignedNetImpactSoMixedPlayerCannotWinBothCards()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var snapshot = new AnalysisSnapshot
        {
            Key = "highlight-test",
            Name = "Highlight Test",
            Season = 2026,
            Week = 4,
            CreatedAt = now,
            Status = AnalysisSnapshotStatuses.Processed
        };
        var mixed = Player("Mixed Signal", "FA", "WR", "Mixed");
        var riser = Player("Clear Riser", "FA", "RB", "Riser");
        var post = new RedditPost
        {
            Snapshot = snapshot,
            RedditId = "post-1",
            Subreddit = "fantasyfootball",
            Title = "Game thread",
            CreatedAt = now,
            CommentCount = 3
        };
        db.AddRange(snapshot, mixed, riser, post);
        await db.SaveChangesAsync();

        AddMention(db, snapshot, post, mixed, "mixed-pos", "Mixed flashed early.", 100, 0.9, 4, TargetAttributionMethods.Explicit, 1);
        AddMention(db, snapshot, post, mixed, "mixed-neg", "Mixed fell apart.", 100, -0.9, 7, TargetAttributionMethods.Explicit, 1);
        AddMention(db, snapshot, post, riser, "riser-pos", "Riser looks great.", 50, 0.8, 3, TargetAttributionMethods.Explicit, 1);
        AddAggregate(db, snapshot, mixed, impactScore: 9.9);
        AddAggregate(db, snapshot, riser, impactScore: 2.4);
        await db.SaveChangesAsync();

        var query = new SnapshotQueryService(
            db,
            Options.Create(new DashboardHighlightOptions
            {
                MinimumMentions = 1,
                MinimumSampleStrength = 0,
                ExcludeLowConfidence = false,
                CommentExcerptLength = 190
            }));

        var highlights = await query.GetHighlightsAsync("highlight-test", CancellationToken.None);

        Assert.NotNull(highlights);
        Assert.Equal("Clear Riser", highlights.Positive?.Name);
        Assert.Equal("Mixed Signal", highlights.Negative?.Name);
        Assert.NotEqual(highlights.Positive?.PlayerId, highlights.Negative?.PlayerId);
    }

    private static FantasySentimentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<FantasySentimentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FantasySentimentDbContext(options);
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

    private static void AddMention(
        FantasySentimentDbContext db,
        AnalysisSnapshot snapshot,
        RedditPost post,
        Player player,
        string redditId,
        string body,
        int redditScore,
        double sentiment,
        double voteWeight,
        string attributionMethod,
        double targetConfidence,
        double semanticConfidence = 0.85,
        string analysisDisposition = AnalysisDispositions.Scored,
        string analysisDispositionReason = AnalysisDispositionReasons.Scored,
        string fantasyActions = "",
        DateTimeOffset? createdAt = null)
    {
        var comment = new RedditComment
        {
            RedditId = redditId,
            Post = post,
            Body = body,
            CreatedAt = createdAt ?? snapshot.CreatedAt.AddMinutes(1),
            Score = redditScore
        };
        db.Add(comment);
        db.PlayerMentions.Add(new PlayerMention
        {
            Snapshot = snapshot,
            Player = player,
            Comment = comment,
            CreatedAt = comment.CreatedAt,
            RawSentimentScore = sentiment,
            FinalSentimentScore = sentiment,
            SentimentScore = sentiment,
            RuleBasedSentimentScore = sentiment,
            FantasyAdjustment = 0,
            SemanticConfidence = semanticConfidence,
            SentimentCategory = TargetedSentimentResult.CategoryFromScore(sentiment),
            SentimentAnalyzer = "Test",
            SentimentContext = body,
            AnalysisDisposition = analysisDisposition,
            AnalysisDispositionReason = analysisDispositionReason,
            MatchedAlias = player.Name,
            TargetAttributionMethod = attributionMethod,
            TargetConfidence = targetConfidence,
            Intent = FantasyIntents.General,
            FantasyActions = fantasyActions,
            RedditScore = redditScore,
            VoteWeight = voteWeight,
            WeightedSentimentContribution = Math.Round(sentiment * voteWeight, 3)
        });
    }

    private static void AddAggregate(
        FantasySentimentDbContext db,
        AnalysisSnapshot snapshot,
        Player player,
        double impactScore)
    {
        var mentions = db.PlayerMentions.Local
            .Where(mention => mention.Player == player)
            .ToList();
        var scoredMentions = mentions
            .Where(mention => mention.AnalysisDisposition == AnalysisDispositions.Scored)
            .ToList();
        var totalVoteWeight = scoredMentions.Sum(mention => mention.VoteWeight);
        var positiveImpact = scoredMentions.Where(mention => mention.FinalSentimentScore > 0)
            .Sum(mention => mention.FinalSentimentScore * mention.VoteWeight);
        var negativeImpact = scoredMentions.Where(mention => mention.FinalSentimentScore < 0)
            .Sum(mention => mention.FinalSentimentScore * mention.VoteWeight);

        db.SnapshotPlayerAggregates.Add(new SnapshotPlayerAggregate
        {
            Snapshot = snapshot,
            Player = player,
            MentionCount = Math.Max(mentions.Count, 1),
            ScoredMentionCount = scoredMentions.Count,
            MentionOnlyCount = mentions.Count - scoredMentions.Count,
            SentimentCoverage = mentions.Count == 0 ? 0 : Math.Round(scoredMentions.Count / (double)mentions.Count, 3),
            HasScoredSentiment = scoredMentions.Count > 0,
            PositiveCount = scoredMentions.Count(mention => mention.FinalSentimentScore > 0.15),
            NeutralCount = scoredMentions.Count(mention => Math.Abs(mention.FinalSentimentScore) <= 0.15),
            NegativeCount = scoredMentions.Count(mention => mention.FinalSentimentScore < -0.15),
            RawSentimentScore = scoredMentions.Count == 0 ? 0 : Math.Round(scoredMentions.Average(mention => mention.RawSentimentScore), 3),
            SentimentScore = scoredMentions.Count == 0 ? 0 : Math.Round(scoredMentions.Average(mention => mention.FinalSentimentScore), 3),
            CommunitySentimentScore = totalVoteWeight == 0
                ? 0
                : Math.Round(scoredMentions.Sum(mention => mention.WeightedSentimentContribution) / totalVoteWeight, 3),
            TotalRedditScore = mentions.Sum(mention => mention.RedditScore),
            TotalVoteWeight = Math.Round(totalVoteWeight, 3),
            AverageVoteWeight = scoredMentions.Count == 0 ? 0 : Math.Round(scoredMentions.Average(mention => mention.VoteWeight), 3),
            SampleStrengthScore = 1,
            Confidence = "High",
            AddCount = CountAction(mentions, FantasyActions.Add),
            DropCount = CountAction(mentions, FantasyActions.Drop),
            StartCount = CountAction(mentions, FantasyActions.Start),
            SitCount = CountAction(mentions, FantasyActions.Sit),
            BuyCount = CountAction(mentions, FantasyActions.Buy),
            SellCount = CountAction(mentions, FantasyActions.Sell),
            HoldCount = CountAction(mentions, FantasyActions.Hold),
            DiscussionShare = 0.1,
            PositiveImpact = Math.Round(positiveImpact, 3),
            NegativeImpact = Math.Round(negativeImpact, 3),
            NetImpact = Math.Round(positiveImpact + negativeImpact, 3),
            ImpactScore = impactScore
        });
    }

    private static int CountAction(IEnumerable<PlayerMention> mentions, string action)
    {
        return mentions.Count(mention => HasLabel(mention.FantasyActions, action));
    }

    private static bool HasLabel(string labels, string expected)
    {
        return labels
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(expected, StringComparer.OrdinalIgnoreCase);
    }

    private static double Community(IEnumerable<WeightedMention> mentions)
    {
        var list = mentions.ToList();
        return Math.Round(list.Sum(mention => mention.Sentiment * mention.VoteWeight) / list.Sum(mention => mention.VoteWeight), 3);
    }

    private sealed record WeightedMention(double Sentiment, double VoteWeight);
}
