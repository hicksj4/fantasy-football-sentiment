namespace FantasySentiment.Api.Options;

public sealed class RedditAnalysisOptions
{
    public int DiscoveryIntervalMinutes { get; set; } = 10;
    public int AnalysisIntervalMinutes { get; set; } = 2;
    public int MaxPostsPerAnalysisBatch { get; set; } = 3;
    public int IncrementalMinimumNewComments { get; set; } = 1;
    public NormalPostAnalysisOptions NormalPosts { get; set; } = new();
    public ImportantNewsAnalysisOptions ImportantNews { get; set; } = new();
    public GameThreadAnalysisOptions GameThreads { get; set; } = new();
}

public sealed class NormalPostAnalysisOptions
{
    public int MinimumComments { get; set; } = 100;
    public int MinimumCommentsWithScore { get; set; } = 40;
    public int MinimumPostScore { get; set; } = 50;
}

public sealed class ImportantNewsAnalysisOptions
{
    public int MinimumComments { get; set; } = 30;
    public string[] Keywords { get; set; } =
    [
        "injury",
        "injured",
        "ir",
        "out",
        "questionable",
        "doubtful",
        "trade",
        "suspension",
        "depth chart",
        "starter",
        "backup"
    ];
}

public sealed class GameThreadAnalysisOptions
{
    public int MinimumComments { get; set; } = 300;
    public int MinimumAgeMinutes { get; set; } = 120;
    public int CooldownWindowMinutes { get; set; } = 15;
    public int MaximumNewCommentsDuringCooldown { get; set; } = 20;
    public string[] TitlePatterns { get; set; } =
    [
        "game thread",
        "post game thread",
        "monday night football",
        "sunday night football",
        "thursday night football"
    ];
}
