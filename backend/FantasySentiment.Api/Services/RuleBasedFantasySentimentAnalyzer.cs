namespace FantasySentiment.Api.Services;

public sealed class RuleBasedFantasySentimentAnalyzer(
    FantasySentimentAnalyzer fantasySentimentAnalyzer) : ISentimentAnalyzer
{
    public ValueTask<TargetedSentimentResult> AnalyzeAsync(
        TargetedSentimentRequest request,
        CancellationToken cancellationToken)
    {
        var result = fantasySentimentAnalyzer.Analyze(request.Context);
        return ValueTask.FromResult(new TargetedSentimentResult(
            result.Score,
            result.Score,
            result.Category,
            result.Score,
            null,
            0,
            nameof(RuleBasedFantasySentimentAnalyzer),
            request.Context));
    }
}
