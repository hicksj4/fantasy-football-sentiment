namespace FantasySentiment.Api.Options;

public sealed class SentimentOptions
{
    public string Mode { get; set; } = SentimentModes.Comparison;
    public bool EnableComparisonDiagnostics { get; set; } = true;
    public RobertaSentimentOptions Roberta { get; set; } = new();
}

public static class SentimentModes
{
    public const string RuleBased = "RuleBased";
    public const string Roberta = "Roberta";
    public const string Hybrid = "Hybrid";
    public const string Comparison = "Comparison";
}

public sealed class RobertaSentimentOptions
{
    public bool Enabled { get; set; }
    public string? OnnxModelPath { get; set; }
    public string? TokenizerPath { get; set; }
}
