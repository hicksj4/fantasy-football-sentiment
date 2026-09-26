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
        double targetConfidence)
    {
        var comment = new RedditComment
        {
            RedditId = redditId,
            Post = post,
            Body = body,
            CreatedAt = snapshot.CreatedAt.AddMinutes(1),
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
            SentimentCategory = TargetedSentimentResult.CategoryFromScore(sentiment),
            SentimentAnalyzer = "Test",
            SentimentContext = body,
            MatchedAlias = player.Name,
            TargetAttributionMethod = attributionMethod,
            TargetConfidence = targetConfidence,
            Intent = FantasyIntents.General,
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
        var totalVoteWeight = mentions.Sum(mention => mention.VoteWeight);
        var positiveImpact = mentions.Where(mention => mention.FinalSentimentScore > 0)
            .Sum(mention => mention.FinalSentimentScore * mention.VoteWeight);
        var negativeImpact = mentions.Where(mention => mention.FinalSentimentScore < 0)
            .Sum(mention => mention.FinalSentimentScore * mention.VoteWeight);

        db.SnapshotPlayerAggregates.Add(new SnapshotPlayerAggregate
        {
            Snapshot = snapshot,
            Player = player,
            MentionCount = Math.Max(mentions.Count, 1),
            PositiveCount = mentions.Count(mention => mention.FinalSentimentScore > 0.15),
            NeutralCount = mentions.Count(mention => Math.Abs(mention.FinalSentimentScore) <= 0.15),
            NegativeCount = mentions.Count(mention => mention.FinalSentimentScore < -0.15),
            RawSentimentScore = mentions.Count == 0 ? 0 : Math.Round(mentions.Average(mention => mention.RawSentimentScore), 3),
            SentimentScore = mentions.Count == 0 ? 0 : Math.Round(mentions.Average(mention => mention.FinalSentimentScore), 3),
            CommunitySentimentScore = totalVoteWeight == 0
                ? 0
                : Math.Round(mentions.Sum(mention => mention.WeightedSentimentContribution) / totalVoteWeight, 3),
            TotalRedditScore = mentions.Sum(mention => mention.RedditScore),
            TotalVoteWeight = Math.Round(totalVoteWeight, 3),
            AverageVoteWeight = mentions.Count == 0 ? 0 : Math.Round(mentions.Average(mention => mention.VoteWeight), 3),
            SampleStrengthScore = 1,
            Confidence = "High",
            DiscussionShare = 0.1,
            PositiveImpact = Math.Round(positiveImpact, 3),
            NegativeImpact = Math.Round(negativeImpact, 3),
            NetImpact = Math.Round(positiveImpact + negativeImpact, 3),
            ImpactScore = impactScore
        });
    }

    private static double Community(IEnumerable<WeightedMention> mentions)
    {
        var list = mentions.ToList();
        return Math.Round(list.Sum(mention => mention.Sentiment * mention.VoteWeight) / list.Sum(mention => mention.VoteWeight), 3);
    }

    private sealed record WeightedMention(double Sentiment, double VoteWeight);
}
