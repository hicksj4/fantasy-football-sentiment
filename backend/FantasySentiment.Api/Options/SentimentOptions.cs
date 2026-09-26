namespace FantasySentiment.Api.Options;

public sealed class SentimentOptions
{
    public string Mode { get; set; } = SentimentModes.Vader;
}

public static class SentimentModes
{
    public const string Vader = "Vader";
    public const string RuleBased = "RuleBased";
}
