using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using FantasySentiment.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Tests;

public sealed class DashboardHighlightsTests
{
    [Fact]
    public async Task Highlights_SelectsMostInfluentialPositiveComment()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var player = AddPlayer(db, "Christian McCaffrey", "SF", "RB");
        AddMovementSnapshots(db, player, now, -0.1, 0.55);
        AddMention(db, player, "Monday Night Football Game Thread", "CMC is fine.", 0.35, 12, now.AddMinutes(-40));
        AddMention(db, player, "Monday Night Football Game Thread", "CMC is a beast, love him.", 0.91, 512, now.AddMinutes(-20));
        await db.SaveChangesAsync();

        var highlights = await CreateService(db).GetHighlightsAsync(TimeSpan.FromHours(24), CancellationToken.None);

        Assert.NotNull(highlights.Riser);
        Assert.Equal(player.Id, highlights.Riser.PlayerId);
        Assert.NotNull(highlights.Riser.TopInfluence);
        Assert.Contains("CMC is a beast", highlights.Riser.TopInfluence.CommentExcerpt);
        Assert.True(highlights.Riser.TopInfluence.SentimentContribution > 0);
    }

    [Fact]
    public async Task Highlights_SelectsMostInfluentialNegativeComment()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var player = AddPlayer(db, "Justin Jefferson", "MIN", "WR");
        AddMovementSnapshots(db, player, now, 0.4, -0.42);
        AddMention(db, player, "Monday Night Football Game Thread", "Jefferson is a little shaky.", -0.35, 22, now.AddMinutes(-50));
        AddMention(db, player, "Monday Night Football Game Thread", "Jefferson is actually cooked for the season.", -0.88, 403, now.AddMinutes(-15));
        await db.SaveChangesAsync();

        var highlights = await CreateService(db).GetHighlightsAsync(TimeSpan.FromHours(24), CancellationToken.None);

        Assert.NotNull(highlights.Faller);
        Assert.Equal(player.Id, highlights.Faller.PlayerId);
        Assert.NotNull(highlights.Faller.TopInfluence);
        Assert.Contains("actually cooked", highlights.Faller.TopInfluence.CommentExcerpt);
        Assert.True(highlights.Faller.TopInfluence.SentimentContribution < 0);
    }

    [Fact]
    public async Task Highlights_CalculatesCommentContributionFromWeightedAverage()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var player = AddPlayer(db, "Puka Nacua", "LAR", "WR");
        AddMovementSnapshots(db, player, now, 0, 0.5);
        var top = AddMention(db, player, "Usage Watch", "Puka looks elite.", 0.8, 99, now.AddMinutes(-20));
        var other = AddMention(db, player, "Usage Watch", "Puka is steady.", 0.2, 9, now.AddMinutes(-10));
        await db.SaveChangesAsync();

        var highlights = await CreateService(db).GetHighlightsAsync(TimeSpan.FromHours(24), CancellationToken.None);
        var totalWeight = top.VoteWeight + other.VoteWeight;
        var expectedContribution = Math.Round(top.FinalSentimentScore * top.VoteWeight / totalWeight, 3);

        Assert.NotNull(highlights.Riser?.TopInfluence);
        Assert.Equal(expectedContribution, highlights.Riser.TopInfluence.SentimentContribution);
    }

    [Fact]
    public async Task Highlights_CommentContributionsSumApproximatelyToCommunitySentiment()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var player = AddPlayer(db, "Amon-Ra St. Brown", "DET", "WR");
        AddMovementSnapshots(db, player, now, -0.1, 0.4);
        AddMention(db, player, "Sun God Thread", "Amon-Ra is automatic.", 0.7, 70, now.AddMinutes(-45));
        AddMention(db, player, "Sun God Thread", "Amon-Ra is still safe.", 0.3, 10, now.AddMinutes(-35));
        AddMention(db, player, "Sun God Thread", "Amon-Ra target share is a concern.", -0.2, 4, now.AddMinutes(-25));
        await db.SaveChangesAsync();

        var highlights = await CreateService(db).GetHighlightsAsync(TimeSpan.FromHours(24), CancellationToken.None);
        var mentions = await db.PlayerMentions
            .Where(mention => mention.PlayerId == player.Id)
            .ToListAsync();
        var totalWeight = mentions.Sum(mention => mention.VoteWeight);
        var contributionSum = Math.Round(
            mentions.Sum(mention => mention.FinalSentimentScore * mention.VoteWeight / totalWeight),
            3);

        Assert.NotNull(highlights.Riser);
        Assert.Equal(contributionSum, highlights.Riser.CommunitySentiment);
    }

    [Fact]
    public async Task Highlights_ExcludesLowSamplePlayers()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var player = AddPlayer(db, "One Comment Wonder", "FA", "WR");
        AddMovementSnapshots(db, player, now, -0.5, 1);
        AddMention(db, player, "Tiny Sample Thread", "One Comment Wonder is the best player alive.", 1, 900, now.AddMinutes(-5));
        await db.SaveChangesAsync();

        var options = new DashboardHighlightOptions
        {
            MinimumMentions = 5,
            MinimumSampleStrength = 0,
            ExcludeLowConfidence = false,
            MinimumAbsoluteSentimentChange = 0.01
        };
        var highlights = await CreateService(db, options).GetHighlightsAsync(TimeSpan.FromHours(24), CancellationToken.None);

        Assert.Null(highlights.Riser);
        Assert.Null(highlights.Faller);
    }

    [Fact]
    public async Task Highlights_TimeWindowCanChangeHighlightedPlayers()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var recent = AddPlayer(db, "Recent Riser", "KC", "WR");
        var weekly = AddPlayer(db, "Weekly Riser", "BUF", "RB");

        AddMovementSnapshots(db, recent, now, 0.05, 0.32, firstOffset: TimeSpan.FromMinutes(-50));
        AddMention(db, recent, "Late News", "Recent Riser looks great tonight.", 0.7, 40, now.AddMinutes(-20));
        AddMovementSnapshots(db, weekly, now, -0.25, 0.75, firstOffset: TimeSpan.FromDays(-2));
        AddMention(db, weekly, "Two Day Old Thread", "Weekly Riser is a league winner now.", 0.95, 80, now.AddDays(-2).AddMinutes(15));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var oneHour = await service.GetHighlightsAsync(TimeSpan.FromHours(1), CancellationToken.None);
        var sevenDays = await service.GetHighlightsAsync(TimeSpan.FromDays(7), CancellationToken.None);

        Assert.Equal(recent.Id, oneHour.Riser?.PlayerId);
        Assert.Equal(weekly.Id, sevenDays.Riser?.PlayerId);
    }

    [Fact]
    public async Task Highlights_UsesDiminishingReturnVoteWeightForHighRawScores()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var player = AddPlayer(db, "Drake London", "ATL", "WR");
        AddMovementSnapshots(db, player, now, -0.1, 0.5);
        var viral = AddMention(db, player, "Falcons Thread", "London finally arrived.", 0.8, 500, now.AddMinutes(-30));
        var normal = AddMention(db, player, "Falcons Thread", "London looks solid.", 0.8, 50, now.AddMinutes(-20));
        await db.SaveChangesAsync();

        var highlights = await CreateService(db).GetHighlightsAsync(TimeSpan.FromHours(24), CancellationToken.None);

        Assert.NotNull(highlights.Riser?.TopInfluence);
        Assert.Equal(Math.Round(viral.VoteWeight, 3), highlights.Riser.TopInfluence.VoteWeight);
        Assert.True(viral.VoteWeight < normal.VoteWeight * 1.4);
        Assert.True(viral.VoteWeight < 4);
    }

    [Fact]
    public async Task Highlights_OneViralCommentDoesNotCompletelyDominateSentiment()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var player = AddPlayer(db, "Volatile Player", "DAL", "RB");
        AddMovementSnapshots(db, player, now, -0.1, 0.2);
        AddMention(db, player, "Viral Thread", "Volatile Player broke fantasy tonight.", 0.95, 1_000_000, now.AddMinutes(-30));
        for (var i = 0; i < 5; i++)
        {
            AddMention(db, player, "Viral Thread", $"Volatile Player still has workload concerns {i}.", -0.15, 30, now.AddMinutes(-25 + i));
        }
        await db.SaveChangesAsync();

        var highlights = await CreateService(db).GetHighlightsAsync(TimeSpan.FromHours(24), CancellationToken.None);

        Assert.NotNull(highlights.Riser?.TopInfluence);
        Assert.True(highlights.Riser.TopInfluence.SentimentContribution < 0.35);
        Assert.True(highlights.Riser.CommunitySentiment < 0.35);
    }

    private static FantasySentimentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<FantasySentimentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new FantasySentimentDbContext(options);
    }

    private static AnalyticsQueryService CreateService(
        FantasySentimentDbContext db,
        DashboardHighlightOptions? options = null)
    {
        options ??= new DashboardHighlightOptions
        {
            MinimumMentions = 1,
            MinimumSampleStrength = 0,
            ExcludeLowConfidence = false,
            MinimumAbsoluteSentimentChange = 0.01,
            CommentExcerptLength = 190
        };

        return new AnalyticsQueryService(db, Options.Create(options));
    }

    private static Player AddPlayer(
        FantasySentimentDbContext db,
        string name,
        string team,
        string position)
    {
        var player = new Player
        {
            Name = name,
            Team = team,
            Position = position,
            IsActive = true
        };
        db.Players.Add(player);
        db.SaveChanges();
        return player;
    }

    private static void AddMovementSnapshots(
        FantasySentimentDbContext db,
        Player player,
        DateTimeOffset now,
        double firstSentiment,
        double lastSentiment,
        TimeSpan? firstOffset = null)
    {
        db.PlayerSnapshots.AddRange(
            Snapshot(player.Id, now + (firstOffset ?? TimeSpan.FromHours(-23)), firstSentiment),
            Snapshot(player.Id, now, lastSentiment));
    }

    private static PlayerSnapshot Snapshot(int playerId, DateTimeOffset timestamp, double sentiment)
    {
        return new PlayerSnapshot
        {
            PlayerId = playerId,
            Timestamp = timestamp,
            MentionCount = 8,
            PositiveCount = sentiment > 0 ? 8 : 0,
            NegativeCount = sentiment < 0 ? 8 : 0,
            NeutralCount = sentiment == 0 ? 8 : 0,
            RawSentimentScore = sentiment,
            SentimentScore = sentiment,
            CommunitySentimentScore = sentiment,
            TotalRedditScore = 100,
            TotalVoteWeight = 12,
            AverageVoteWeight = 1.5,
            SampleStrengthScore = 0.4,
            Confidence = "Medium",
            BuzzScore = 1.4
        };
    }

    private static PlayerMention AddMention(
        FantasySentimentDbContext db,
        Player player,
        string postTitle,
        string body,
        double sentiment,
        int redditScore,
        DateTimeOffset createdAt)
    {
        var post = db.RedditPosts.Local.FirstOrDefault(post => post.Title == postTitle)
                   ?? db.RedditPosts.FirstOrDefault(post => post.Title == postTitle);
        if (post is null)
        {
            post = new RedditPost
            {
                RedditId = $"post-{Guid.NewGuid():N}"[..20],
                Subreddit = "fantasyfootball",
                Title = postTitle,
                CreatedAt = createdAt.AddMinutes(-10),
                CommentCount = 1,
                LastObservedAt = createdAt
            };
            db.RedditPosts.Add(post);
            db.SaveChanges();
        }

        var voteWeight = new VoteWeightCalculator().Calculate(redditScore);
        var comment = new RedditComment
        {
            RedditId = $"comment-{Guid.NewGuid():N}"[..28],
            Post = post,
            Body = body,
            CreatedAt = createdAt,
            Score = redditScore
        };
        var mention = new PlayerMention
        {
            PlayerId = player.Id,
            Comment = comment,
            CreatedAt = createdAt,
            RawSentimentScore = sentiment,
            FinalSentimentScore = sentiment,
            SentimentScore = sentiment,
            RuleBasedSentimentScore = sentiment,
            SentimentCategory = TargetedSentimentResult.CategoryFromScore(sentiment),
            SentimentAnalyzer = "Test",
            SentimentContext = body,
            MatchedAlias = player.Name,
            Intent = FantasyIntents.General,
            RedditScore = redditScore,
            VoteWeight = voteWeight,
            WeightedSentimentContribution = Math.Round(sentiment * voteWeight, 3)
        };

        db.RedditComments.Add(comment);
        db.PlayerMentions.Add(mention);
        return mention;
    }
}
