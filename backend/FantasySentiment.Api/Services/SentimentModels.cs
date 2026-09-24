using FantasySentiment.Api.Domain;

namespace FantasySentiment.Api.Services;

public interface ISentimentAnalyzer
{
    ValueTask<TargetedSentimentResult> AnalyzeAsync(
        TargetedSentimentRequest request,
        CancellationToken cancellationToken);
}

public sealed record TargetedSentimentRequest(
    string FullText,
    string Context,
    PlayerMentionMatch Mention);

public sealed record TargetedSentimentResult(
    double RawSentimentScore,
    double FinalSentimentScore,
    string SentimentCategory,
    double RuleBasedScore,
    double? ModelScore,
    double FantasyAdjustment,
    string AnalyzerName,
    string Context,
    string? Diagnostics = null)
{
    public static string CategoryFromScore(double score)
    {
        return score switch
        {
            >= 0.15 => SentimentCategories.Positive,
            <= -0.15 => SentimentCategories.Negative,
            _ => SentimentCategories.Neutral
        };
    }
}

public sealed record ModelSentimentResult(
    bool IsAvailable,
    double Score,
    string AnalyzerName,
    string? Diagnostics = null);
