using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantasySentiment.Tests;

public sealed class CommentTargetResolverTests
{
    private readonly CommentTargetResolver _resolver = new(
        new PlayerMentionDetector(),
        NullLogger<CommentTargetResolver>.Instance);

    [Fact]
    public void TitleTarget_TopLevelPronoun_InheritsThreadTitleTarget()
    {
        var resolution = Resolve(
            "Yes, he is bad.",
            "Justin Jefferson: 7 targets, 0 catches");

        var target = Assert.Single(resolution.Targets);
        Assert.Equal("Justin Jefferson", target.PlayerName);
        Assert.Equal(TargetAttributionMethods.ThreadTitleInherited, target.AttributionMethod);
    }

    [Fact]
    public void ReplyChain_InheritsAlreadyResolvedParentTarget()
    {
        var parent = Resolve(
            "Yes, he is bad.",
            "Justin Jefferson: 7 targets, 0 catches");

        var child = Resolve(
            "I also think he is terrible.",
            "Justin Jefferson: 7 targets, 0 catches",
            parent.Targets);

        Assert.Equal("Justin Jefferson", Assert.Single(parent.Targets).PlayerName);
        var childTarget = Assert.Single(child.Targets);
        Assert.Equal("Justin Jefferson", childTarget.PlayerName);
        Assert.Equal(TargetAttributionMethods.AncestorInherited, childTarget.AttributionMethod);
    }

    [Fact]
    public void EllipticalReply_InheritsSingleParentTarget()
    {
        var parent = Resolve("Is Kyle Pitts droppable?", "Game thread");
        var child = Resolve(
            "Always has been and always will be.",
            "Game thread",
            parent.Targets);

        var target = Assert.Single(child.Targets);
        Assert.Equal("Kyle Pitts", target.PlayerName);
        Assert.Equal(TargetAttributionMethods.ParentInherited, target.AttributionMethod);
    }

    [Fact]
    public void ParentInheritance_DoesNotRequireQuestionMark()
    {
        var parent = Resolve("How sustainable is this Watson run", "Game thread");
        var child = Resolve(
            "He is their #1 by far.",
            "Game thread",
            parent.Targets);

        Assert.Equal("Christian Watson", Assert.Single(child.Targets).PlayerName);
    }

    [Fact]
    public void ExplicitCurrentCommentMention_Wins()
    {
        var resolution = Resolve("Kyle Pitts is completely washed.", "Game thread");

        var target = Assert.Single(resolution.Targets);
        Assert.Equal("Kyle Pitts", target.PlayerName);
        Assert.Equal(TargetAttributionMethods.Explicit, target.AttributionMethod);
    }

    [Fact]
    public void ExplicitChildTarget_OverridesInheritedTopic()
    {
        var parent = Resolve("Kyle Pitts looks terrible.", "Game thread");
        var child = Resolve(
            "McBride looks way better.",
            "Game thread",
            parent.Targets);

        var target = Assert.Single(child.Targets);
        Assert.Equal("Trey McBride", target.PlayerName);
        Assert.Equal(TargetAttributionMethods.Explicit, target.AttributionMethod);
    }

    [Fact]
    public void ExplicitReplyTarget_ResetsInheritedConversationTopic()
    {
        var first = Resolve("Jefferson has been awful.", "Game thread");
        var second = Resolve(
            "At least Addison is good.",
            "Game thread",
            first.Targets);
        var third = Resolve(
            "Yep, he's solid.",
            "Game thread",
            second.Targets);

        Assert.Equal("Justin Jefferson", Assert.Single(first.Targets).PlayerName);
        var secondTarget = Assert.Single(second.Targets);
        Assert.Equal("Jordan Addison", secondTarget.PlayerName);
        Assert.Equal(TargetAttributionMethods.Explicit, secondTarget.AttributionMethod);

        var thirdTarget = Assert.Single(third.Targets);
        Assert.Equal("Jordan Addison", thirdTarget.PlayerName);
        Assert.Equal(TargetAttributionMethods.ParentInherited, thirdTarget.AttributionMethod);
    }

    [Fact]
    public void ExplicitRiskyAlias_OverridesInheritedTopicWhenProperNameContextExists()
    {
        var parent = Resolve(
            "People are going to talk about Golden not catching but most of the passes to him were dogshit.",
            "Game thread");
        var child = Resolve(
            "Yeah, Love is looking bad out there.",
            "Game thread",
            parent.Targets);

        Assert.Equal("Matthew Golden", Assert.Single(parent.Targets).PlayerName);
        var childTarget = Assert.Single(child.Targets);
        Assert.Equal("Jordan Love", childTarget.PlayerName);
        Assert.Equal(TargetAttributionMethods.Explicit, childTarget.AttributionMethod);
    }

    [Fact]
    public void CommonWordAlias_LowercaseLove_DoesNotCreatePlayerMention()
    {
        var resolution = Resolve("I love this offense.", "Game thread");

        Assert.Empty(resolution.Targets);
    }

    [Fact]
    public void CommonWordAlias_DoesNotStealSentimentFromExplicitPlayer()
    {
        var resolution = Resolve("I absolutely love Bijan.", "Game thread");

        var target = Assert.Single(resolution.Targets);
        Assert.Equal("Bijan Robinson", target.PlayerName);
    }

    [Theory]
    [InlineData("Love stinks")]
    [InlineData("Is Love okay?")]
    public void RiskyLoveAlias_UsesPlayerWhenQuarterbackContextExists(string text)
    {
        var resolution = Resolve(text, "Game thread");

        var target = Assert.Single(resolution.Targets);
        Assert.Equal("Jordan Love", target.PlayerName);
    }

    [Theory]
    [InlineData("Love that dude, Bijan")]
    [InlineData("I love Bijan")]
    [InlineData("Love the usage for Bijan")]
    public void RiskyLoveAlias_DoesNotStealGenericPositiveLanguage(string text)
    {
        var resolution = Resolve(text, "Game thread");

        var target = Assert.Single(resolution.Targets);
        Assert.Equal("Bijan Robinson", target.PlayerName);
    }

    [Fact]
    public void RiskyLoveAlias_CanCoexistWithExplicitReceiverTarget()
    {
        var resolution = Resolve("Love is killing my Kraft stonks", "Game thread");

        Assert.Collection(
            resolution.Targets,
            first => Assert.Equal("Jordan Love", first.PlayerName),
            second => Assert.Equal("Tucker Kraft", second.PlayerName));
    }

    [Fact]
    public void AmbiguousTitle_DoesNotInferTarget()
    {
        var resolution = Resolve(
            "He was awful.",
            "CMC and Purdy struggled tonight.");

        Assert.Empty(resolution.Targets);
        Assert.Equal("ambiguous thread title targets", resolution.UnresolvedReason);
    }

    [Fact]
    public void AmbiguousParent_DoesNotInferTarget()
    {
        var parent = Resolve("CMC looked great but Purdy was awful.", "Game thread");
        var child = Resolve(
            "Yeah he really was.",
            "Game thread",
            parent.Targets);

        Assert.Equal(2, parent.Targets.Count);
        Assert.Empty(child.Targets);
        Assert.Equal("ambiguous inherited targets", child.UnresolvedReason);
    }

    [Fact]
    public void MultipleExplicitPlayers_CurrentComment_ReturnsEachTarget()
    {
        var resolution = Resolve("CMC looked great but Purdy was awful.", "Game thread");

        Assert.Collection(
            resolution.Targets,
            first =>
            {
                Assert.Equal("Christian McCaffrey", first.PlayerName);
                Assert.Equal(TargetAttributionMethods.Explicit, first.AttributionMethod);
            },
            second =>
            {
                Assert.Equal("Brock Purdy", second.PlayerName);
                Assert.Equal(TargetAttributionMethods.Explicit, second.AttributionMethod);
            });
    }

    private CommentTargetResolution Resolve(
        string comment,
        string title,
        IReadOnlyList<ResolvedCommentTarget>? parentTargets = null)
    {
        return _resolver.Resolve(new CommentTargetResolutionRequest(
            "comment-id",
            comment,
            title,
            Players(),
            parentTargets ?? []));
    }

    private static IReadOnlyList<Player> Players()
    {
        return
        [
            Player(1, "Justin Jefferson", "MIN", "WR", "Jefferson", "JJettas"),
            Player(2, "Kyle Pitts", "ATL", "TE", "Pitts"),
            Player(3, "Christian Watson", "GB", "WR", "Watson"),
            Player(4, "Trey McBride", "ARI", "TE", "McBride"),
            Player(5, "Christian McCaffrey", "SF", "RB", "CMC", "McCaffrey"),
            Player(6, "Brock Purdy", "SF", "QB", "Purdy"),
            Player(7, "Jordan Addison", "MIN", "WR", "Addison"),
            Player(8, "Matthew Golden", "GB", "WR", "Golden"),
            Player(9, "Jordan Love", "GB", "QB", "Love"),
            Player(10, "Bijan Robinson", "ATL", "RB", "Bijan"),
            Player(11, "Tucker Kraft", "GB", "TE", "Kraft", "Tucker Kraft")
        ];
    }

    private static Player Player(
        int id,
        string name,
        string team,
        string position,
        params string[] aliases)
    {
        return new Player
        {
            Id = id,
            Name = name,
            Team = team,
            Position = position,
            IsActive = true,
            Aliases = aliases.Select(alias => new PlayerAlias { Alias = alias }).ToList()
        };
    }
}
