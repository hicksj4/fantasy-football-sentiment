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

    [Fact]
    public async Task GenericLowPointExclamation_IsNotAutomaticallySarcasm()
    {
        var result = await AnalyzeAsync("Wow 5 points on that play Bijan", "Bijan Robinson");

        Assert.True(result.Detail.FinalScore > -0.15, result.Detail.ToDiagnostics());
        Assert.DoesNotContain(result.Detail.AppliedRules, rule => rule.StartsWith("sarcasm:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FreakOfNature_IsPositivePhrase()
    {
        var result = await AnalyzeAsync("Henry continues to be a freak of nature I guess", "Derrick Henry");

        Assert.True(result.Detail.FinalScore > 0.15, result.Detail.ToDiagnostics());
        Assert.Contains("freak of nature", result.Detail.MatchedPhrases);
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

    [Theory]
    [InlineData("JSN is him", "Jaxon Smith-Njigba")]
    [InlineData("JSN is the truth", "Jaxon Smith-Njigba")]
    [InlineData("CMC we're so back", "Christian McCaffrey")]
    [InlineData("JSN to the moon", "Jaxon Smith-Njigba")]
    [InlineData("JSN LFGGGG", "Jaxon Smith-Njigba")]
    public async Task ModernPositiveSlang_IsPositive(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.True(result.Detail.FinalScore > 0.15);
    }

    [Theory]
    [InlineData("Herbert is straight ass", "Justin Herbert")]
    [InlineData("Pickens is invisible", "George Pickens")]
    [InlineData("Loveland is buns", "Colston Loveland")]
    public async Task ModernNegativeSlang_IsNegative(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
    }

    [Theory]
    [InlineData("Penix is him...who sucks.", "Michael Penix Jr.")]
    [InlineData("Bijan is not in fact a beast", "Bijan Robinson")]
    [InlineData("this Marshawn is not in fact Beastmode", "Marshawn Lloyd")]
    public async Task TargetLocalReversal_BeatsPositiveSlang(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
        Assert.Contains(result.Detail.AppliedRules, rule => rule.Contains("reversal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Watson gets the first TD!", "Christian Watson")]
    [InlineData("MATTHEW GOLDEN TD. FUCKING WATCH", "Matthew Golden")]
    [InlineData("they feed watson in the end zone", "Christian Watson")]
    [InlineData("Golden making a catch? Is that legal?", "Matthew Golden")]
    [InlineData("We got a target!! London Truthers FEASTING!!", "Drake London")]
    [InlineData("Just need a Bijan breakaway run tonight, it’ll happen", "Bijan Robinson")]
    public async Task FantasyProductionEvents_ArePositiveForTarget(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.True(result.Detail.FinalScore > 0.15, result.Detail.ToDiagnostics());
    }

    [Theory]
    [InlineData("Need Kraft to do more", "Tucker Kraft")]
    [InlineData("Feed Kraft pls", "Tucker Kraft")]
    [InlineData("Started London Yikes", "Drake London")]
    [InlineData("I wish I could drop London", "Drake London")]
    [InlineData("Fucking Penix", "Michael Penix Jr.")]
    [InlineData("Love is killing my Kraft stonks", "Tucker Kraft")]
    public async Task UsageAndLineupConcern_AreNegativeForTarget(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
    }

    [Fact]
    public async Task BetterThanComparison_GivesDifferentDirectionToEachPlayer()
    {
        var golden = await AnalyzeAsync("Golden is better than Watson", "Matthew Golden");
        var watson = await AnalyzeAsync("Golden is better than Watson", "Christian Watson");

        Assert.True(golden.Detail.FinalScore > 0.15, golden.Detail.ToDiagnostics());
        Assert.True(watson.Detail.FinalScore < -0.15, watson.Detail.ToDiagnostics());
    }

    [Theory]
    [InlineData("Bijan is so damn fun to watch", "Bijan Robinson")]
    [InlineData("I would die for Bijan", "Bijan Robinson")]
    [InlineData("Bijan said fuck it, I carry this team on my shoulders", "Bijan Robinson")]
    [InlineData("Bijan always on Madden mode", "Bijan Robinson")]
    [InlineData("Bijan is the whole team", "Bijan Robinson")]
    [InlineData("Bijan is that dude", "Bijan Robinson")]
    [InlineData("Gibbs clears", "Jahmyr Gibbs")]
    [InlineData("Bijan you beautiful bastard", "Bijan Robinson")]
    public async Task ModernPerformancePraise_IsPositiveForTarget(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.True(result.Detail.FinalScore > 0.15, result.Detail.ToDiagnostics());
    }

    [Fact]
    public async Task ShortSubstrings_DoNotMatchInsideOtherWords()
    {
        var result = await AnalyzeAsync("Herbert is a class act.", "Justin Herbert");

        Assert.DoesNotContain("ass", result.Detail.MatchedNegativeTerms);
        Assert.True(result.Detail.FinalScore > -0.15);
    }

    [Fact]
    public async Task GarbageTime_DoesNotMakeProductionNegative()
    {
        var result = await AnalyzeAsync("Herbert got fed in garbage time.", "Justin Herbert");

        Assert.DoesNotContain("garbage", result.Detail.MatchedNegativeTerms);
        Assert.True(result.Detail.FinalScore > -0.15);
    }

    [Theory]
    [InlineData("JSN", "Jaxon Smith-Njigba")]
    [InlineData("Holy shit Herbert", "Justin Herbert")]
    [InlineData("Holy fuck CMC", "Christian McCaffrey")]
    [InlineData("Damn Bijan", "Bijan Robinson")]
    public async Task NameOnlyReactions_AreNeutral(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.Equal(0, result.Detail.FinalScore);
        Assert.Contains("nameOnlyReactionNeutral", result.Detail.AppliedRules);
    }

    [Theory]
    [InlineData("Holy fuck CMC is awful", "Christian McCaffrey")]
    [InlineData("Damn Bijan looks terrible", "Bijan Robinson")]
    public async Task DirectionalExclamations_StillUseEvaluation(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
    }

    [Theory]
    [InlineData("Herbert lmao", "Justin Herbert")]
    [InlineData("lmaoooo Herbert", "Justin Herbert")]
    public async Task NameOnlyLmaoMockery_IsMildlyNegative(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
        Assert.Contains(result.Detail.AppliedRules, rule => rule.StartsWith("nameOnlyMockeryNegative", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LmaoWithDirectionalProduction_IsNotGloballyNegative()
    {
        var result = await AnalyzeAsync("Bijan just broke three tackles lmao", "Bijan Robinson");

        Assert.True(result.Detail.FinalScore > -0.15, result.Detail.ToDiagnostics());
        Assert.DoesNotContain(result.Detail.AppliedRules, rule => rule.StartsWith("nameOnlyMockeryNegative", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Why did I start Ladd?", "Ladd McConkey")]
    [InlineData("I started Ladd 🤡", "Ladd McConkey")]
    public async Task LineupRegret_IsNegativeForOwnRosterTarget(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.Equal(TargetPerspectives.OwnRoster, result.Context.Perspective);
        Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
        Assert.Contains(result.Detail.AppliedRules, rule => rule.StartsWith("lineupRegret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OwnRosterHype_IsPositiveForEachMentionedPlayer()
    {
        const string text = "Have JSN + McBride let’s goooo";

        foreach (var playerName in new[] { "Jaxon Smith-Njigba", "Trey McBride" })
        {
            var result = await AnalyzeAsync(text, playerName);

            Assert.Equal(TargetPerspectives.OwnRoster, result.Context.Perspective);
            Assert.True(result.Detail.FinalScore > 0.15);
        }
    }

    [Theory]
    [InlineData("So George Pickens is…..fantasy irrelevant", "George Pickens", "fantasy irrelevant")]
    [InlineData("Tucker Kraft welcome to the waiver wire buddy", "Tucker Kraft", "welcome to the waiver wire")]
    public async Task ExistingFantasyPhrases_FireThroughProductionPath(
        string text,
        string playerName,
        string expectedPhrase)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
        Assert.Contains(expectedPhrase, result.Detail.MatchedPhrases);
    }

    [Fact]
    public async Task SaveMeFrom_AssignsOppositeDirections()
    {
        var lamb = await AnalyzeAsync("CeeDee save me from colston loveland", "CeeDee Lamb");
        var loveland = await AnalyzeAsync("CeeDee save me from colston loveland", "Colston Loveland");

        Assert.True(lamb.Detail.FinalScore > 0.15, lamb.Detail.ToDiagnostics());
        Assert.True(loveland.Detail.FinalScore < -0.15, loveland.Detail.ToDiagnostics());
        Assert.Contains(lamb.Detail.AppliedRules, rule => rule.StartsWith("saveMePositive", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(loveland.Detail.AppliedRules, rule => rule.StartsWith("saveMeFromNegative", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FantasyTierAtBest_IsNegative()
    {
        var result = await AnalyzeAsync("Barring a big turnaround, Herbert is a QB2 at best", "Justin Herbert");

        Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
        Assert.Contains(result.Detail.AppliedRules, rule => rule.StartsWith("fantasyTierAtBest", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(FantasyIntents.Drop, result.Intent);
    }

    [Theory]
    [InlineData("Herbert is a solid QB2 in superflex", "Justin Herbert")]
    [InlineData("London is a good WR2", "Drake London")]
    public async Task SolidTierLanguage_IsNotNegative(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.True(result.Detail.FinalScore > -0.15, result.Detail.ToDiagnostics());
        Assert.DoesNotContain(result.Detail.AppliedRules, rule => rule.StartsWith("fantasyTierAtBest", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Herbert is only a QB2", "Justin Herbert")]
    [InlineData("Herbert is nothing more than a QB2", "Justin Herbert")]
    public async Task CappedTierLanguage_IsNegativeWithoutDropIntent(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
        Assert.Equal(FantasyIntents.General, result.Intent);
        Assert.Contains(result.Detail.AppliedRules, rule => rule.StartsWith("fantasyTierAtBest", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DirectDropLanguage_IsTargetScopedNegative()
    {
        var result = await AnalyzeAsync("I'm dropping Rome", "Rome Odunze");

        Assert.Equal(TargetPerspectives.OwnRoster, result.Context.Perspective);
        Assert.Equal(FantasyIntents.Drop, result.Intent);
        Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
    }

    [Fact]
    public async Task DroppedForReplacement_AssignsOppositeIntentsAndSentiment()
    {
        const string text = "I dropped Schultz for Loveland";

        var schultz = await AnalyzeAsync(text, "Dalton Schultz");
        var loveland = await AnalyzeAsync(text, "Colston Loveland");

        Assert.Equal(FantasyIntents.Drop, schultz.Intent);
        Assert.Equal(FantasyIntents.Add, loveland.Intent);
        Assert.True(schultz.Detail.FinalScore < -0.15, schultz.Detail.ToDiagnostics());
        Assert.True(loveland.Detail.FinalScore > -0.15, loveland.Detail.ToDiagnostics());
    }

    [Fact]
    public async Task WaiverDropLanguage_IsNegativeAndScoped()
    {
        var result = await AnalyzeAsync("Bijan to the waiver wire", "Bijan Robinson");

        Assert.Equal(FantasyIntents.Drop, result.Intent);
        Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
    }

    [Fact]
    public async Task WaiverAddLanguage_IsPositiveAndScoped()
    {
        var result = await AnalyzeAsync("Pick up Bijan off waivers", "Bijan Robinson");

        Assert.Equal(FantasyIntents.Add, result.Intent);
        Assert.True(result.Detail.FinalScore > 0.15, result.Detail.ToDiagnostics());
    }

    [Theory]
    [InlineData("Started Reed AMA", "Jayden Reed", FantasyIntents.Start, -1)]
    [InlineData("Drafted Pitts AMA", "Kyle Pitts", FantasyIntents.General, -1)]
    [InlineData("Benched Bijan AMA", "Bijan Robinson", FantasyIntents.Sit, 1)]
    public async Task AmaRegretLanguage_IsTargetScoped(
        string text,
        string playerName,
        string expectedIntent,
        int expectedDirection)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.Equal(TargetPerspectives.OwnRoster, result.Context.Perspective);
        Assert.Equal(expectedIntent, result.Intent);
        if (expectedDirection < 0)
        {
            Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
        }
        else
        {
            Assert.True(result.Detail.FinalScore > 0.15, result.Detail.ToDiagnostics());
        }
    }

    [Fact]
    public async Task MultiTargetBenchFor_AssignsSitAndStartIntent()
    {
        const string text = "I'm benching Odunze and BTJ for Wicks and Vele";

        Assert.Equal(FantasyIntents.Sit, (await AnalyzeAsync(text, "Rome Odunze")).Intent);
        Assert.Equal(FantasyIntents.Sit, (await AnalyzeAsync(text, "Brian Thomas Jr.")).Intent);
        Assert.Equal(FantasyIntents.Start, (await AnalyzeAsync(text, "Dontayvion Wicks")).Intent);
        Assert.Equal(FantasyIntents.Start, (await AnalyzeAsync(text, "Devaughn Vele")).Intent);
    }

    [Fact]
    public async Task BenchNot_AssignsOppositeStartSitIntent()
    {
        const string text = "Bench BTJ not Odunze.";

        Assert.Equal(FantasyIntents.Sit, (await AnalyzeAsync(text, "Brian Thomas Jr.")).Intent);
        Assert.Equal(FantasyIntents.Start, (await AnalyzeAsync(text, "Rome Odunze")).Intent);
    }

    [Fact]
    public async Task BenchedFor_AssignsSitAndStartIntent()
    {
        const string text = "Benched Pitts for Njoku";

        Assert.Equal(FantasyIntents.Sit, (await AnalyzeAsync(text, "Kyle Pitts")).Intent);
        Assert.Equal(FantasyIntents.Start, (await AnalyzeAsync(text, "David Njoku")).Intent);
    }

    [Fact]
    public async Task StartChoiceQuestion_AssignsStartIntentToOptions()
    {
        const string text = "Golden or Odunze?";

        Assert.Equal(FantasyIntents.Start, (await AnalyzeAsync(text, "Matthew Golden")).Intent);
        Assert.Equal(FantasyIntents.Start, (await AnalyzeAsync(text, "Rome Odunze")).Intent);
    }

    [Fact]
    public async Task HypeAndPanicLanguage_DoesNotCreateDiscussionSignals()
    {
        var result = await AnalyzeAsync("Bijan is a monster. Panic was silly.", "Bijan Robinson");

        Assert.Empty(result.Semantics.DiscussionSignals);
        Assert.DoesNotContain(FantasyIntents.Hype, result.Semantics.DiscussionSignals);
        Assert.DoesNotContain(FantasyIntents.Panic, result.Semantics.DiscussionSignals);
        Assert.Equal(FantasyIntents.General, result.Intent);
        Assert.True(result.Detail.FinalScore > 0.15, result.Detail.ToDiagnostics());
    }

    [Fact]
    public async Task TransactionActions_AreSeparateFromDiscussionSignals()
    {
        const string text = "I dropped Schultz for Loveland";

        var schultz = await AnalyzeAsync(text, "Dalton Schultz");
        var loveland = await AnalyzeAsync(text, "Colston Loveland");

        Assert.Contains(FantasyActions.Drop, schultz.Semantics.FantasyActions);
        Assert.Contains(FantasyActions.Add, loveland.Semantics.FantasyActions);
        Assert.Empty(schultz.Semantics.DiscussionSignals);
        Assert.Empty(loveland.Semantics.DiscussionSignals);
    }

    [Fact]
    public async Task Injury_IsDiscussionSignalNotFantasyAction()
    {
        var result = await AnalyzeAsync("Pitts is hurt again.", "Kyle Pitts");

        Assert.Contains(FantasyDiscussionSignals.Injury, result.Semantics.DiscussionSignals);
        Assert.Empty(result.Semantics.FantasyActions);
        Assert.Equal(FantasyIntents.Injury, result.Intent);
    }

    [Fact]
    public async Task OpponentLossLanguage_IsOpponentPerspectiveAndPositiveForTarget()
    {
        const string text = "My team might lose to Watson and Gibbs.";

        foreach (var playerName in new[] { "Christian Watson", "Jahmyr Gibbs" })
        {
            var result = await AnalyzeAsync(text, playerName);

            Assert.Equal(TargetPerspectives.OpponentRoster, result.Context.Perspective);
            Assert.True(result.Detail.FinalScore > 0.15, result.Detail.ToDiagnostics());
        }
    }

    [Fact]
    public async Task GenericPlayingAgainstLanguage_DoesNotForceOpponentPerspective()
    {
        var result = await AnalyzeAsync("Prayer circle for whoever is playing against Gibbs.", "Jahmyr Gibbs");

        Assert.Equal(TargetPerspectives.Unknown, result.Context.Perspective);
    }

    [Fact]
    public async Task StartingToThink_DoesNotCreateOwnRosterOrStartAction()
    {
        var result = await AnalyzeAsync("Starting to think Watson is not good.", "Christian Watson");

        Assert.Equal(TargetPerspectives.Unknown, result.Context.Perspective);
        Assert.DoesNotContain(FantasyActions.Start, result.Semantics.FantasyActions);
        Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
    }

    [Fact]
    public async Task RegrettedDrop_IsPositiveButStillDropAction()
    {
        var result = await AnalyzeAsync("Just dropped Wicks 🤦", "Dontayvion Wicks");

        Assert.Contains(FantasyActions.Drop, result.Semantics.FantasyActions);
        Assert.True(result.Detail.FinalScore > 0.15, result.Detail.ToDiagnostics());
    }

    [Theory]
    [InlineData("Pitts was a steal in the mid rounds.", 1)]
    [InlineData("Pitts was bait as a draft pick.", -1)]
    public async Task DraftValueLanguage_IsStructuralSentiment(string text, int expectedDirection)
    {
        var result = await AnalyzeAsync(text, "Kyle Pitts");

        if (expectedDirection > 0)
        {
            Assert.True(result.Detail.FinalScore > 0.15, result.Detail.ToDiagnostics());
        }
        else
        {
            Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
        }
    }

    [Theory]
    [InlineData("Where is Kraft?", -1)]
    [InlineData("Kraft is getting fed.", 1)]
    public async Task UsageOpportunityLanguage_IsTargetSpecific(string text, int expectedDirection)
    {
        var result = await AnalyzeAsync(text, "Tucker Kraft");

        if (expectedDirection > 0)
        {
            Assert.True(result.Detail.FinalScore > 0.15, result.Detail.ToDiagnostics());
        }
        else
        {
            Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
        }
    }

    [Theory]
    [InlineData("I'm facing JSN and I'm cooked", "Jaxon Smith-Njigba")]
    [InlineData("JSN is killing me", "Jaxon Smith-Njigba")]
    public async Task OpponentRosterThreat_IsPositiveForTheOpponentPlayer(string text, string playerName)
    {
        var result = await AnalyzeAsync(text, playerName);

        Assert.Equal(TargetPerspectives.OpponentRoster, result.Context.Perspective);
        Assert.True(result.Detail.FinalScore > 0.15, result.Detail.ToDiagnostics());
        Assert.Contains(result.Detail.AppliedRules, rule => rule.StartsWith("opponentThreat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BoundedComparison_GivesDifferentDirectionToEachPlayer()
    {
        var puka = await AnalyzeAsync("Taking Puka over JSN is going to haunt me.", "Puka Nacua");
        var jsn = await AnalyzeAsync("Taking Puka over JSN is going to haunt me.", "Jaxon Smith-Njigba");

        Assert.True(puka.Detail.FinalScore < -0.15, puka.Detail.ToDiagnostics());
        Assert.True(jsn.Detail.FinalScore > 0.15, jsn.Detail.ToDiagnostics());
    }

    [Fact]
    public async Task MissedChoiceComparison_GivesBenefitAndRegretDirections()
    {
        var lamb = await AnalyzeAsync("I could have had Lamb, but I chose Barkley instead.", "CeeDee Lamb");
        var barkley = await AnalyzeAsync("I could have had Lamb, but I chose Barkley instead.", "Saquon Barkley");

        Assert.True(lamb.Detail.FinalScore > 0.15, lamb.Detail.ToDiagnostics());
        Assert.True(barkley.Detail.FinalScore < -0.15, barkley.Detail.ToDiagnostics());
    }

    [Fact]
    public async Task ShouldHaveStartedComparison_GivesBenefitAndRegretDirections()
    {
        var washington = await AnalyzeAsync("Def should have started Washington over Ladd 🥲", "Parker Washington");
        var ladd = await AnalyzeAsync("Def should have started Washington over Ladd 🥲", "Ladd McConkey");

        Assert.Equal(TargetPerspectives.OwnRoster, washington.Context.Perspective);
        Assert.Equal(TargetPerspectives.OwnRoster, ladd.Context.Perspective);
        Assert.True(washington.Detail.FinalScore > 0.15, washington.Detail.ToDiagnostics());
        Assert.True(ladd.Detail.FinalScore < -0.15, ladd.Detail.ToDiagnostics());
    }

    [Fact]
    public async Task ReplyAgreement_InheritsParentSentiment()
    {
        var result = await AnalyzeReplyAsync("Pitts is washed.", "yeah absolutely", "Kyle Pitts");

        Assert.True(result.Detail.FinalScore < -0.15);
        Assert.Contains(result.Detail.AppliedRules, rule => rule.StartsWith("replyAgreement", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReplyAgreement_InheritsFantasyIrrelevantParent()
    {
        var result = await AnalyzeReplyAsync(
            "So George Pickens is…..fantasy irrelevant",
            "seems like it yeah",
            "George Pickens");

        Assert.True(result.Detail.FinalScore < -0.15, result.Detail.ToDiagnostics());
        Assert.Contains(result.Detail.AppliedRules, rule => rule.StartsWith("replyAgreement", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReplyDisagreement_CanFlipNegativeParent()
    {
        var result = await AnalyzeReplyAsync("Pitts is washed.", "nah he'll be fine", "Kyle Pitts");

        Assert.True(result.Detail.FinalScore > 0.15);
        Assert.Contains(result.Detail.AppliedRules, rule => rule.StartsWith("replyDisagreement", StringComparison.OrdinalIgnoreCase));
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
            context.Perspective,
            context.Target.AttributionMethod);
        var result = await _sentimentAnalyzer.AnalyzeAsync(request, CancellationToken.None);
        var detail = _sentimentAnalyzer.AnalyzeDetailed(request);
        var semanticClassification = _intentClassifier.ClassifyAll(text, context.TargetText, match);

        Assert.Equal(detail.FinalScore, result.FinalSentimentScore);
        return new AnalyzedTarget(context, detail, semanticClassification);
    }

    private async Task<AnalyzedTarget> AnalyzeReplyAsync(
        string parent,
        string reply,
        string playerName)
    {
        var parentTargets = _detector.DetectMentions(parent, Players())
            .Select(match => ResolvedCommentTarget.FromMatch(
                match,
                TargetAttributionMethods.Explicit,
                1.0))
            .ToList();
        var inherited = parentTargets
            .Single(target => target.PlayerName == playerName)
            .InheritForCurrentComment(TargetAttributionMethods.ParentInherited, 0.90);
        var context = _targetContextBuilder.Build(
            "Game thread",
            parent,
            reply,
            inherited,
            [inherited]);
        var match = context.Target.ToMatch();
        var analysisContext = _threadContextBuilder.Build(context);
        var request = new TargetedSentimentRequest(
            reply,
            analysisContext,
            match,
            context.TargetText,
            context.Perspective,
            context.Target.AttributionMethod);
        var result = await _sentimentAnalyzer.AnalyzeAsync(request, CancellationToken.None);
        var detail = _sentimentAnalyzer.AnalyzeDetailed(request);
        var semanticClassification = _intentClassifier.ClassifyAll(reply, context.TargetText, match);

        Assert.Equal(detail.FinalScore, result.FinalSentimentScore);
        return new AnalyzedTarget(context, detail, semanticClassification);
    }

    private static SentimentLexiconOptions Lexicon()
    {
        return new SentimentLexiconOptions
        {
            PositiveTerms = [ "great", "incredible", "monster", "elite", "beast", "love", "truth", "fine" ],
            NegativeTerms = [ "awful", "terrible", "bad", "bust", "useless", "garbage" ],
            PositivePhrases =
            [
                "league winner",
                "smash start",
                "must start",
                "automatic start",
                "locked in",
                "breakout game",
                "set and forget",
                "is him",
                "is the truth",
                "freak of nature",
                "we're so back",
                "to the moon",
                "lets go"
            ],
            NegativePhrases =
            [
                "roster clogger",
                "goose egg",
                "completely washed",
                "dead roster spot",
                "absolute bust",
                "looks awful",
                "looks terrible",
                "straight ass",
                "is ass",
                "is invisible",
                "is buns",
                "fantasy irrelevant",
                "welcome to the waiver wire"
            ],
            PositiveSlang = [ "disgusting", "filthy", "cooking", "lfg" ],
            NegativeSlang = [ "washed", "unstartable", "cooked", "ass", "buns", "mid", "fraud", "bum" ],
            Intensifiers = [ "very", "absolutely", "completely" ],
            Negators = [ "not", "never", "no", "don't", "dont" ]
        };
    }

    private static IntentLexiconOptions Intents()
    {
        return new IntentLexiconOptions
        {
            Add = [ "add", "waiver" ],
            Drop = [ "drop", "welcome to the waiver wire" ],
            Start = [ "start", "started" ],
            Sit = [ "sit", "bench", "unstartable" ],
            Buy = [ "buy" ],
            Sell = [ "sell", "selling", "sell high" ],
            Hold = [ "hold" ],
            Injury = [ "hurt" ],
            Hype = [ "league winner", "monster", "cooking", "lets go", "lfg", "from the top rope" ],
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
            Player(7, "Christian McCaffrey", "SF", "RB", "CMC", "McCaffrey"),
            Player(8, "Jaxon Smith-Njigba", "SEA", "WR", "JSN", "Smith-Njigba"),
            Player(9, "Justin Herbert", "LAC", "QB", "Herbert"),
            Player(10, "George Pickens", "DAL", "WR", "Pickens"),
            Player(11, "Colston Loveland", "CHI", "TE", "Loveland"),
            Player(12, "Ladd McConkey", "LAC", "WR", "Ladd", "Ladd McConkey"),
            Player(13, "Puka Nacua", "LAR", "WR", "Puka"),
            Player(14, "CeeDee Lamb", "DAL", "WR", "CeeDee", "Lamb"),
            Player(15, "Saquon Barkley", "PHI", "RB", "Barkley"),
            Player(16, "Parker Washington", "JAX", "WR", "Washington", "Parker Washington"),
            Player(17, "Trey McBride", "ARI", "TE", "McBride"),
            Player(18, "Tucker Kraft", "GB", "TE", "Kraft", "Tucker Kraft"),
            Player(19, "Rome Odunze", "CHI", "WR", "Rome", "Odunze", "Rome Odunze"),
            Player(20, "Brian Thomas Jr.", "JAX", "WR", "BTJ", "Brian Thomas", "Brian Thomas Jr."),
            Player(21, "Dontayvion Wicks", "GB", "WR", "Wicks", "Dontayvion Wicks"),
            Player(22, "Devaughn Vele", "NO", "WR", "Vele", "Devaughn Vele"),
            Player(23, "Marshawn Lloyd", "GB", "RB", "Marshawn", "Lloyd"),
            Player(24, "Jordan Love", "GB", "QB", "Love", "Jordan Love"),
            Player(25, "Christian Watson", "GB", "WR", "Watson", "Christian Watson"),
            Player(26, "Jahmyr Gibbs", "DET", "RB", "Gibbs", "Jahmyr Gibbs"),
            Player(27, "David Njoku", "CLE", "TE", "Njoku", "David Njoku"),
            Player(28, "Michael Penix Jr.", "ATL", "QB", "Penix", "Michael Penix"),
            Player(29, "Derrick Henry", "BAL", "RB", "Henry", "Derrick Henry"),
            Player(30, "Dalton Schultz", "HOU", "TE", "Schultz", "Dalton Schultz"),
            Player(31, "Jayden Reed", "GB", "WR", "Reed", "Jayden Reed"),
            Player(32, "Kyle Allen", "DET", "QB", "Allen", "Kyle Allen")
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
        FantasySemanticClassification Semantics)
    {
        public string Intent => Semantics.LegacyIntent;
    }
}
