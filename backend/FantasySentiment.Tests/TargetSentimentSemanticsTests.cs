using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using FantasySentiment.Api.Services;

namespace FantasySentiment.Tests;

public sealed class TargetSentimentSemanticsTests
{
    private readonly PlayerMentionDetector _detector = new();
    private readonly TargetContextBuilder _targetContextBuilder = new(new MentionContextExtractor());
    private readonly ThreadContextBuilder _threadContextBuilder = new();
    private readonly VaderSentimentAnalyzer _sentimentAnalyzer =
        new(new FantasySentimentAnalyzer(new TestOptionsMonitor<SentimentLexiconOptions>(Lexicon())));
    private readonly FantasyIntentClassifier _intentClassifier =
        new(new TestOptionsMonitor<IntentLexiconOptions>(Intents()));

    [Theory]
    [InlineData("Bijan 📈", "Bijan Robinson")]
    [InlineData("Bijan 🔥", "Bijan Robinson")]
    [InlineData("Bijan 🐐", "Bijan Robinson")]
    public async Task DomainEmoji_MakeTargetSentimentPositive(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.True(result.Detail.FinalScore > 0.15);
        Assert.True(result.Detail.EmojiAdjustment > 0);
        Assert.Contains("emoji:", result.Detail.ToDiagnostics());
    }

    [Fact]
    public async Task FireEmoji_IsRemovedBeforeVaderAndScoredSeparately()
    {
        var result = await AnalyzeAsync("Bijan 🔥", "Bijan Robinson");

        Assert.Equal("Bijan", result.Detail.VaderText);
        Assert.Equal(0, result.Detail.RawVaderScore);
        Assert.True(result.Detail.EmojiAdjustment > 0);
        Assert.True(result.Detail.FinalScore > 0);
    }

    [Theory]
    [InlineData("Bijan 📉", "Bijan Robinson")]
    [InlineData("Bijan is cooked 💀", "Bijan Robinson")]
    [InlineData("Pitts is washed 📉", "Kyle Pitts")]
    public async Task DomainEmoji_CanReinforceNegativeTargetSentiment(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.True(result.Detail.FinalScore < -0.15);
        Assert.True(result.Detail.EmojiAdjustment < 0 || result.Detail.FantasyAdjustment < 0);
    }

    [Fact]
    public async Task OpponentThreatLanguage_TurnsAuthorDespairIntoPositiveTargetSentiment()
    {
        const string text = "My opponent has Bijan, London and Golden. Fantasy week already over 😢";

        foreach (var playerName in new[] { "Bijan Robinson", "Drake London", "Matthew Golden" })
        {
            var result = await AnalyzeAsync(text, playerName);

            Assert.Equal(TargetPerspectives.OpponentRoster, result.Context.Perspective);
            Assert.True(result.Detail.RawVaderScore < 0);
            Assert.True(result.Detail.FinalScore > 0.15);
            Assert.Contains(result.Detail.AppliedRules, rule => rule.StartsWith("opponentThreat", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData("My opponent has Pitts and he looks awful.", "Kyle Pitts")]
    [InlineData("My opponent has Bijan and he looks terrible.", "Bijan Robinson")]
    public async Task OpponentRoster_DoesNotInvertExplicitNegativeEvaluation(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.Equal(TargetPerspectives.OpponentRoster, result.Context.Perspective);
        Assert.True(result.Detail.FinalScore < -0.15);
        Assert.DoesNotContain(result.Detail.AppliedRules, rule => rule.StartsWith("opponentThreat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OwnRosterPositiveLanguage_IsPositive()
    {
        var result = await AnalyzeAsync("As a Drake London owner, I'm beyond happy.", "Drake London");

        Assert.Equal(TargetPerspectives.OwnRoster, result.Context.Perspective);
        Assert.True(result.Detail.FinalScore > 0.15);
    }

    [Fact]
    public async Task FacingRegret_IsPositiveForOpponentPlayer()
    {
        var result = await AnalyzeAsync("I wish I didn't play against Golden tonight.", "Matthew Golden");

        Assert.Equal(TargetPerspectives.OpponentRoster, result.Context.Perspective);
        Assert.True(result.Detail.FinalScore > 0.15);
    }

    [Fact]
    public async Task SellIntent_DoesNotForceNegativeSentiment()
    {
        const string text = "Should I sell James Cook after this monster game?";

        var result = await AnalyzeAsync(text, "James Cook");

        Assert.Equal(FantasyIntents.Sell, result.Intent);
        Assert.True(result.Detail.FinalScore > 0.15);
    }

    [Fact]
    public async Task ExplicitNegativeLanguage_BeatsSellIntent()
    {
        const string text = "James Cook is washed. I'm selling.";

        var result = await AnalyzeAsync(text, "James Cook");

        Assert.Equal(FantasyIntents.Sell, result.Intent);
        Assert.True(result.Detail.FinalScore < -0.15);
    }

    [Fact]
    public async Task DropQuestion_IsOnlyWeakNegativeWithoutExplicitEvaluation()
    {
        var result = await AnalyzeAsync("Should I drop Pitts?", "Kyle Pitts");

        Assert.Equal(FantasyIntents.Drop, result.Intent);
        Assert.InRange(result.Detail.FinalScore, -0.20, 0.05);
    }

    [Fact]
    public async Task DirectWashedAndUnstartable_IsStronglyNegative()
    {
        var weakQuestion = await AnalyzeAsync("Should I drop Pitts?", "Kyle Pitts");
        var directAssertion = await AnalyzeAsync("Pitts is completely washed and unstartable.", "Kyle Pitts");

        Assert.True(directAssertion.Detail.FinalScore < -0.50);
        Assert.True(directAssertion.Detail.FinalScore < weakQuestion.Detail.FinalScore);
        Assert.Contains("completely washed", directAssertion.Detail.MatchedPhrases);
        Assert.DoesNotContain("washed", directAssertion.Detail.MatchedNegativeTerms);
    }

    [Fact]
    public async Task LowPointSarcasm_IsNegative()
    {
        var result = await AnalyzeAsync("Thanks for the 2 points, Adams.", "Davante Adams");

        Assert.True(result.Detail.FinalScore < -0.15);
        Assert.Contains(result.Detail.AppliedRules, rule => rule.StartsWith("sarcasm:", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("CMC is disgusting.")]
    [InlineData("CMC is disgusting 🔥")]
    [InlineData("Bijan is a monster 🔥")]
    public async Task FantasySlangAndEmoji_CombineAsPositiveTargetSentiment(string text)
    {
        var playerName = text.StartsWith("CMC", StringComparison.OrdinalIgnoreCase)
            ? "Christian McCaffrey"
            : "Bijan Robinson";

        var result = await AnalyzeAsync(text, playerName);

        Assert.True(result.Detail.FinalScore > 0.15);
    }

    [Fact]
    public async Task SameThreatPhrase_UsesRosterPerspective()
    {
        var opponent = await AnalyzeAsync("My opponent has Bijan. He ended my week.", "Bijan Robinson");
        var ownRoster = await AnalyzeAsync("I started Pitts. He ended my week.", "Kyle Pitts");

        Assert.Equal(TargetPerspectives.OpponentRoster, opponent.Context.Perspective);
        Assert.Equal(TargetPerspectives.OwnRoster, ownRoster.Context.Perspective);
        Assert.True(opponent.Detail.FinalScore > 0.15);
        Assert.True(ownRoster.Detail.FinalScore < -0.15);
    }

    [Fact]
    public async Task DiagnosticResult_ExposesLayeredAdjustments()
    {
        var result = await AnalyzeAsync("Bijan 📈", "Bijan Robinson");

        Assert.Equal(0, result.Detail.RawVaderScore);
        Assert.Equal(0, result.Detail.FantasyAdjustment);
        Assert.True(result.Detail.EmojiAdjustment > 0);
        Assert.Equal(0, result.Detail.PerspectiveAdjustment);
        Assert.Equal(0, result.Detail.QuestionAdjustment);
        Assert.Equal(result.Detail.EmojiAdjustment, result.Detail.FinalScore);
    }

    private async Task<AnalyzedTarget> AnalyzeAsync(string text, string playerName)
    {
        var targets = _detector.DetectMentions(text, Players())
            .Select(match => ResolvedCommentTarget.FromMatch(
                match,
                TargetAttributionMethods.Explicit,
                1.0))
            .ToList();
        var target = targets.Single(target => target.PlayerName == playerName);
        var context = _targetContextBuilder.Build(
            "Game thread",
            null,
            text,
            target,
            targets);
        var match = context.Target.ToMatch();
        var analysisContext = _threadContextBuilder.Build(context);
        var request = new TargetedSentimentRequest(
            text,
            analysisContext,
            match,
            context.TargetText,
            context.Perspective);
        var result = await _sentimentAnalyzer.AnalyzeAsync(request, CancellationToken.None);
        var detail = _sentimentAnalyzer.AnalyzeDetailed(request);
        var intent = _intentClassifier.Classify(text, match);

        Assert.Equal(detail.FinalScore, result.FinalSentimentScore);
        return new AnalyzedTarget(context, detail, intent);
    }

    private static SentimentLexiconOptions Lexicon()
    {
        return new SentimentLexiconOptions
        {
            PositiveTerms = [ "great", "incredible", "monster", "elite", "beast", "love" ],
            NegativeTerms = [ "awful", "terrible", "bad", "bust", "useless" ],
            PositivePhrases =
            [
                "league winner",
                "smash start",
                "must start",
                "automatic start",
                "locked in",
                "breakout game",
                "set and forget"
            ],
            NegativePhrases =
            [
                "roster clogger",
                "goose egg",
                "completely washed",
                "dead roster spot",
                "absolute bust",
                "looks awful",
                "looks terrible"
            ],
            PositiveSlang = [ "disgusting", "filthy", "cooking" ],
            NegativeSlang = [ "washed", "unstartable", "cooked" ],
            Intensifiers = [ "very", "absolutely", "completely" ],
            Negators = [ "not", "never", "no", "don't", "dont" ]
        };
    }

    private static IntentLexiconOptions Intents()
    {
        return new IntentLexiconOptions
        {
            Add = [ "add", "waiver" ],
            Drop = [ "drop" ],
            Start = [ "start" ],
            Sit = [ "sit", "bench", "unstartable" ],
            Buy = [ "buy" ],
            Sell = [ "sell", "selling", "sell high" ],
            Hold = [ "hold" ],
            Injury = [ "hurt" ],
            Hype = [ "league winner", "monster", "cooking" ],
            Panic = [ "panic" ]
        };
    }

    private static IReadOnlyList<Player> Players()
    {
        return
        [
            Player(1, "Bijan Robinson", "ATL", "RB", "Bijan"),
            Player(2, "Drake London", "ATL", "WR", "London", "Drake London"),
            Player(3, "Matthew Golden", "GB", "WR", "Golden"),
            Player(4, "Kyle Pitts", "ATL", "TE", "Pitts"),
            Player(5, "James Cook", "BUF", "RB", "James Cook", "Cook"),
            Player(6, "Davante Adams", "LAR", "WR", "Adams"),
            Player(7, "Christian McCaffrey", "SF", "RB", "CMC", "McCaffrey")
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

    private sealed record AnalyzedTarget(
        ResolvedTargetContext Context,
        SentimentAnalysisResult Detail,
        string Intent);
}
