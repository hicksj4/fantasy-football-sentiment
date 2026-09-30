using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using FantasySentiment.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Tests;

public sealed class PlayerMentionAnalysisTests
{
    [Fact]
    public async Task PlayerMentions_ReturnsScoredAndMentionOnlyRows()
    {
        await using var db = CreateDb();
        var (snapshot, player, post) = AddSnapshotPlayerAndPost(db, "comment-analysis");
        AddMention(db, snapshot, post, player, "scored", "Player looks explosive.", 42, 0.72, 2.1);
        AddMention(
            db,
            snapshot,
            post,
            player,
            "mention-only",
            "Player might be invisible.",
            8,
            -0.31,
            1.6,
            semanticConfidence: 0.30,
            disposition: AnalysisDispositions.MentionOnly,
            reason: AnalysisDispositionReasons.LowSemanticConfidence);
        await db.SaveChangesAsync();

        var page = await CreateQuery(db).GetPlayerMentionsAsync(
            snapshot.Key,
            player.Id,
            disposition: null,
            sort: null,
            page: 1,
            pageSize: 20,
            CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal(2, page.TotalMentionCount);
        Assert.Equal(1, page.ScoredMentionCount);
        Assert.Equal(1, page.MentionOnlyCount);
        Assert.Equal(0.5, page.SentimentCoverage);
        Assert.Contains(page.Items, item => item.AnalysisDisposition == AnalysisDispositions.Scored);
        Assert.Contains(page.Items, item => item.AnalysisDisposition == AnalysisDispositions.MentionOnly);
    }

    [Fact]
    public async Task PlayerMentions_WeightedContributionExistsOnlyForScoredMentions()
    {
        await using var db = CreateDb();
        var (snapshot, player, post) = AddSnapshotPlayerAndPost(db, "contribution-scope");
        AddMention(db, snapshot, post, player, "scored", "Player is elite.", 10, 0.5, 2.25);
        AddMention(
            db,
            snapshot,
            post,
            player,
            "mention-only",
            "Player name in a vague sentence.",
            20,
            -0.5,
            3.5,
            semanticConfidence: 0.20,
            disposition: AnalysisDispositions.MentionOnly,
            reason: AnalysisDispositionReasons.LowSemanticConfidence);
        await db.SaveChangesAsync();

        var page = await CreateQuery(db).GetPlayerMentionsAsync(
            snapshot.Key,
            player.Id,
            null,
            "reddit-score",
            1,
            20,
            CancellationToken.None);

        Assert.NotNull(page);
        var scored = Assert.Single(page.Items, item => item.AnalysisDisposition == AnalysisDispositions.Scored);
        var mentionOnly = Assert.Single(page.Items, item => item.AnalysisDisposition == AnalysisDispositions.MentionOnly);
        Assert.Equal(1.125, scored.WeightedContribution);
        Assert.Null(mentionOnly.WeightedContribution);
    }

    [Fact]
    public async Task PlayerMentions_ReturnsParentBodyForInheritedTargets()
    {
        await using var db = CreateDb();
        var (snapshot, player, post) = AddSnapshotPlayerAndPost(db, "parent-context");
        AddMention(
            db,
            snapshot,
            post,
            player,
            "reply",
            "It's only been a quarter.",
            7,
            0,
            1.9,
            targetAttributionMethod: TargetAttributionMethods.ParentInherited,
            parentRedditId: "parent",
            parentBody: "Player looked bad on the first drive.");
        await db.SaveChangesAsync();

        var page = await CreateQuery(db).GetPlayerMentionsAsync(
            snapshot.Key,
            player.Id,
            null,
            null,
            1,
            20,
            CancellationToken.None);

        Assert.NotNull(page);
        var item = Assert.Single(page.Items);
        Assert.Equal("Player looked bad on the first drive.", item.ParentBody);
        Assert.Equal(TargetAttributionMethods.ParentInherited, item.TargetAttributionMethod);
    }

    [Fact]
    public async Task PlayerMentions_PaginatesResults()
    {
        await using var db = CreateDb();
        var (snapshot, player, post) = AddSnapshotPlayerAndPost(db, "pagination");
        AddMention(db, snapshot, post, player, "one", "Player one.", 1, 0.1, 1);
        AddMention(db, snapshot, post, player, "two", "Player two.", 2, 0.2, 1);
        AddMention(db, snapshot, post, player, "three", "Player three.", 3, 0.3, 1);
        await db.SaveChangesAsync();

        var firstPage = await CreateQuery(db).GetPlayerMentionsAsync(snapshot.Key, player.Id, null, "reddit-score", 1, 2, CancellationToken.None);
        var secondPage = await CreateQuery(db).GetPlayerMentionsAsync(snapshot.Key, player.Id, null, "reddit-score", 2, 2, CancellationToken.None);

        Assert.NotNull(firstPage);
        Assert.NotNull(secondPage);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.True(firstPage.HasMore);
        Assert.Single(secondPage.Items);
        Assert.False(secondPage.HasMore);
        Assert.Equal("Player three.", firstPage.Items[0].Body);
        Assert.Equal("Player one.", secondPage.Items[0].Body);
    }

    [Fact]
    public async Task PlayerMentions_AreScopedToSnapshotAndPlayer()
    {
        await using var db = CreateDb();
        var (snapshot, player, post) = AddSnapshotPlayerAndPost(db, "scope-a");
        var (otherSnapshot, _, otherPost) = AddSnapshotPlayerAndPost(db, "scope-b");
        var otherPlayer = new Player { Name = "Other Player", Team = "FA", Position = "WR", IsActive = true };
        db.Players.Add(otherPlayer);
        await db.SaveChangesAsync();

        AddMention(db, snapshot, post, player, "wanted", "Wanted row.", 10, 0.6, 2);
        AddMention(db, otherSnapshot, otherPost, player, "wrong-snapshot", "Wrong snapshot.", 10, 0.6, 2);
        AddMention(db, snapshot, post, otherPlayer, "wrong-player", "Wrong player.", 10, 0.6, 2);
        await db.SaveChangesAsync();

        var page = await CreateQuery(db).GetPlayerMentionsAsync(snapshot.Key, player.Id, null, null, 1, 20, CancellationToken.None);

        Assert.NotNull(page);
        var item = Assert.Single(page.Items);
        Assert.Equal("Wanted row.", item.Body);
    }

    [Fact]
    public async Task PlayerMentions_FiltersByDisposition()
    {
        await using var db = CreateDb();
        var (snapshot, player, post) = AddSnapshotPlayerAndPost(db, "filter");
        AddMention(db, snapshot, post, player, "scored", "Scored row.", 10, 0.4, 2);
        AddMention(
            db,
            snapshot,
            post,
            player,
            "mention-only",
            "Mention-only row.",
            10,
            0.4,
            2,
            semanticConfidence: 0.20,
            disposition: AnalysisDispositions.MentionOnly,
            reason: AnalysisDispositionReasons.LowSemanticConfidence);
        await db.SaveChangesAsync();

        var scored = await CreateQuery(db).GetPlayerMentionsAsync(snapshot.Key, player.Id, "scored", null, 1, 20, CancellationToken.None);
        var mentionOnly = await CreateQuery(db).GetPlayerMentionsAsync(snapshot.Key, player.Id, "mention-only", null, 1, 20, CancellationToken.None);

        Assert.NotNull(scored);
        Assert.NotNull(mentionOnly);
        Assert.Single(scored.Items);
        Assert.Single(mentionOnly.Items);
        Assert.Equal(AnalysisDispositions.Scored, scored.Items[0].AnalysisDisposition);
        Assert.Equal(AnalysisDispositions.MentionOnly, mentionOnly.Items[0].AnalysisDisposition);
    }

    private static FantasySentimentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<FantasySentimentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FantasySentimentDbContext(options);
    }

    private static SnapshotQueryService CreateQuery(FantasySentimentDbContext db)
    {
        return new SnapshotQueryService(
            db,
            Options.Create(new DashboardHighlightOptions()),
            snapshotProcessingOptions: Options.Create(new SnapshotProcessingOptions()));
    }

    private static (AnalysisSnapshot Snapshot, Player Player, RedditPost Post) AddSnapshotPlayerAndPost(
        FantasySentimentDbContext db,
        string key)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new AnalysisSnapshot
        {
            Key = key,
            Name = key,
            Season = 2026,
            Week = Math.Abs(key.GetHashCode()) % 1000,
            CreatedAt = now,
            Status = AnalysisSnapshotStatuses.Processed
        };
        var player = new Player
        {
            Name = $"Player {key}",
            Team = "FA",
            Position = "RB",
            IsActive = true
        };
        var post = new RedditPost
        {
            Snapshot = snapshot,
            RedditId = $"post-{key}",
            Subreddit = "fantasyfootball",
            Title = "Game thread",
            CreatedAt = now,
            CommentCount = 1
        };
        db.AddRange(snapshot, player, post);
        db.SaveChanges();
        return (snapshot, player, post);
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
        double semanticConfidence = 0.85,
        string disposition = AnalysisDispositions.Scored,
        string reason = AnalysisDispositionReasons.Scored,
        string targetAttributionMethod = TargetAttributionMethods.Explicit,
        string? parentRedditId = null,
        string? parentBody = null)
    {
        if (!string.IsNullOrWhiteSpace(parentRedditId) && !string.IsNullOrWhiteSpace(parentBody))
        {
            db.RedditComments.Add(new RedditComment
            {
                RedditId = parentRedditId,
                Post = post,
                Body = parentBody,
                CreatedAt = snapshot.CreatedAt,
                Score = 1
            });
        }

        var comment = new RedditComment
        {
            RedditId = $"{snapshot.Key}-{redditId}",
            Post = post,
            ParentRedditId = parentRedditId,
            Body = body,
            CreatedAt = snapshot.CreatedAt.AddMinutes(db.RedditComments.Local.Count + 1),
            Score = redditScore
        };
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
            SentimentDiagnostics = "semanticConfidence=0.85; rules=test-rule",
            AnalysisDisposition = disposition,
            AnalysisDispositionReason = reason,
            MatchedAlias = player.Name,
            TargetAttributionMethod = targetAttributionMethod,
            TargetConfidence = 1,
            TargetPerspective = TargetPerspectives.Unknown,
            Intent = FantasyIntents.General,
            FantasyActions = FantasyActions.Add,
            DiscussionSignals = FantasyDiscussionSignals.Injury,
            RedditScore = redditScore,
            VoteWeight = voteWeight,
            WeightedSentimentContribution = Math.Round(sentiment * voteWeight, 3)
        });
    }
}
