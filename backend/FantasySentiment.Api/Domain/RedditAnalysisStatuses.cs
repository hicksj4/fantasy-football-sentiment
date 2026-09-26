namespace FantasySentiment.Api.Domain;

public static class RedditAnalysisStatuses
{
    public const string Discovered = "Discovered";
    public const string Collecting = "Collecting";
    public const string ReadyForAnalysis = "ReadyForAnalysis";
    public const string Analyzing = "Analyzing";
    public const string Analyzed = "Analyzed";
}

public static class RedditPostTypes
{
    public const string Normal = "Normal";
    public const string ImportantNews = "ImportantNews";
    public const string GameThread = "GameThread";
    public const string PostGameThread = "PostGameThread";
}
