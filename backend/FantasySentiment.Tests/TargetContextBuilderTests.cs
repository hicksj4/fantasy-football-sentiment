using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Services;

namespace FantasySentiment.Tests;

public sealed class TargetContextBuilderTests
{
    private readonly PlayerMentionDetector _detector = new();
    private readonly TargetContextBuilder _builder = new(new MentionContextExtractor());

    [Fact]
    public void SeparateClauses_GetDifferentTargetContextAndPerspective()
    {
        const string text =
            "Listen, as a Drake London owner, I'm beyond happy. But damn, I wish I didn't play against Golden tonight.";
        var targets = DetectTargets(text);

        var london = Build(text, targets.Single(target => target.PlayerName == "Drake London"), targets);
        var golden = Build(text, targets.Single(target => target.PlayerName == "Matthew Golden"), targets);

        Assert.NotEqual(london.TargetText, golden.TargetText);
        Assert.Contains("beyond happy", london.TargetText);
        Assert.DoesNotContain("play against Golden", london.TargetText);
        Assert.Contains("play against Golden", golden.TargetText);
        Assert.DoesNotContain("beyond happy", golden.TargetText);
        Assert.Equal(TargetPerspectives.OwnRoster, london.Perspective);
        Assert.Equal(TargetPerspectives.OpponentRoster, golden.Perspective);
    }

    [Fact]
    public void StrongContrastSentence_UsesTargetSpecificClause()
    {
        const string text = "London was incredible but Golden was awful.";
        var targets = DetectTargets(text);

        var london = Build(text, targets.Single(target => target.PlayerName == "Drake London"), targets);
        var golden = Build(text, targets.Single(target => target.PlayerName == "Matthew Golden"), targets);

        Assert.Equal("London was incredible", london.TargetText);
        Assert.Equal("Golden was awful.", golden.TargetText);
    }

    [Fact]
    public void SharedOpponentRosterStatement_AppliesSameContextToAllTargets()
    {
        const string text = "My opponent has Bijan, London and Golden. Fantasy week already over 😢";
        var targets = DetectTargets(text);

        var contexts = targets
            .Select(target => Build(text, target, targets))
            .ToList();

        Assert.Equal(3, contexts.Count);
        Assert.All(contexts, context =>
        {
            Assert.Equal(TargetPerspectives.OpponentRoster, context.Perspective);
            Assert.Contains("My opponent has Bijan, London and Golden.", context.TargetText);
            Assert.Contains("Fantasy week already over 😢", context.TargetText);
        });
        Assert.Single(contexts.Select(context => context.TargetText).Distinct());
    }

    [Fact]
    public void EmojiContext_IsPreserved()
    {
        const string text = "Bijan 📈";
        var target = Assert.Single(DetectTargets(text));

        var context = Build(text, target, [target]);

        Assert.Equal("Bijan Robinson", context.Target.PlayerName);
        Assert.Contains("📈", context.TargetText);
    }

    [Fact]
    public void ComparisonStatement_KeepsSharedContextForBothTargets()
    {
        const string text = "Def should have started Washington over Ladd 🥲";
        var targets = DetectTargets(text);

        var washington = Build(text, targets.Single(target => target.PlayerName == "Parker Washington"), targets);
        var ladd = Build(text, targets.Single(target => target.PlayerName == "Ladd McConkey"), targets);

        Assert.Equal(washington.TargetText, ladd.TargetText);
        Assert.Equal(TargetPerspectives.OwnRoster, washington.Perspective);
        Assert.Equal(TargetPerspectives.OwnRoster, ladd.Perspective);
    }

    [Theory]
    [InlineData("I'm facing JSN and I'm cooked")]
    [InlineData("JSN is killing me")]
    public void OpponentRosterPatterns_AreRecognized(string text)
    {
        var target = Assert.Single(DetectTargets(text));
        var context = Build(text, target, [target]);

        Assert.Equal(TargetPerspectives.OpponentRoster, context.Perspective);
    }

    [Fact]
    public void OwnRosterHavePattern_AppliesToEachTarget()
    {
        const string text = "Have JSN + McBride let’s goooo";
        var targets = DetectTargets(text);

        var contexts = targets.Select(target => Build(text, target, targets)).ToList();

        Assert.Equal(2, contexts.Count);
        Assert.All(contexts, context => Assert.Equal(TargetPerspectives.OwnRoster, context.Perspective));
    }

    [Fact]
    public void MixedStartedAndFacingStatement_SplitsOwnAndOpponentTargets()
    {
        const string text = "Guess who started Caleb and Loveland and is facing JSN and Lamb 😀";
        var targets = DetectTargets(text);

        var caleb = Build(text, targets.Single(target => target.PlayerName == "Caleb Williams"), targets);
        var loveland = Build(text, targets.Single(target => target.PlayerName == "Colston Loveland"), targets);
        var jsn = Build(text, targets.Single(target => target.PlayerName == "Jaxon Smith-Njigba"), targets);
        var lamb = Build(text, targets.Single(target => target.PlayerName == "CeeDee Lamb"), targets);

        Assert.Equal(TargetPerspectives.OwnRoster, caleb.Perspective);
        Assert.Equal(TargetPerspectives.OwnRoster, loveland.Perspective);
        Assert.Equal(TargetPerspectives.OpponentRoster, jsn.Perspective);
        Assert.Equal(TargetPerspectives.OpponentRoster, lamb.Perspective);
        Assert.Contains("started Caleb and Loveland", caleb.TargetText);
        Assert.Contains("started Caleb and Loveland", loveland.TargetText);
        Assert.DoesNotContain("facing JSN and Lamb", caleb.TargetText);
        Assert.DoesNotContain("facing JSN and Lamb", loveland.TargetText);
        Assert.Contains("is facing JSN and Lamb", jsn.TargetText);
        Assert.Contains("is facing JSN and Lamb", lamb.TargetText);
        Assert.DoesNotContain("started Caleb", jsn.TargetText);
        Assert.DoesNotContain("started Caleb", lamb.TargetText);
    }

    [Fact]
    public void EllipsisContinuation_StaysWithTargetContext()
    {
        const string text = "So George Pickens is…..fantasy irrelevant";
        var target = Assert.Single(DetectTargets(text));

        var context = Build(text, target, [target]);

        Assert.Contains("fantasy irrelevant", context.TargetText);
    }

    private ResolvedTargetContext Build(
        string text,
        ResolvedCommentTarget target,
        IReadOnlyList<ResolvedCommentTarget> targets)
    {
        return _builder.Build(
            "Game thread",
            null,
            text,
            target,
            targets);
    }

    private IReadOnlyList<ResolvedCommentTarget> DetectTargets(string text)
    {
        return _detector.DetectMentions(text, Players())
            .Select(match => ResolvedCommentTarget.FromMatch(
                match,
                TargetAttributionMethods.Explicit,
                1.0))
            .ToList();
    }

    private static IReadOnlyList<Player> Players()
    {
        return
        [
            Player(1, "Bijan Robinson", "ATL", "RB", "Bijan"),
            Player(2, "Drake London", "ATL", "WR", "London", "Drake London"),
            Player(3, "Matthew Golden", "GB", "WR", "Golden"),
            Player(4, "Jaxon Smith-Njigba", "SEA", "WR", "JSN", "Smith-Njigba"),
            Player(5, "Parker Washington", "JAX", "WR", "Washington", "Parker Washington"),
            Player(6, "Ladd McConkey", "LAC", "WR", "Ladd", "Ladd McConkey"),
            Player(7, "Trey McBride", "ARI", "TE", "McBride"),
            Player(8, "Caleb Williams", "CHI", "QB", "Caleb", "Caleb Williams"),
            Player(9, "Colston Loveland", "CHI", "TE", "Loveland", "Colston Loveland"),
            Player(10, "CeeDee Lamb", "DAL", "WR", "CeeDee", "Lamb"),
            Player(11, "George Pickens", "DAL", "WR", "Pickens", "George Pickens")
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
