namespace FantasySentiment.Api.Options;

public sealed class RedditOptions
{
    public bool UseMockReddit { get; set; } = true;
    public string[] Subreddits { get; set; } = ["fantasyfootball"];
    public int IngestionIntervalSeconds { get; set; } = 60;
    public int FetchLookbackMinutes { get; set; } = 90;
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string UserAgent { get; set; } = "FantasySentimentTerminal/0.1 by local-dev";
}
