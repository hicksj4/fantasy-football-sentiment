namespace FantasySentiment.Api.Services;

public sealed class HybridSentimentAnalyzer(
    FantasySentimentAnalyzer fantasySentimentAnalyzer,
    RobertaSentimentAnalyzer robertaSentimentAnalyzer) : ISentimentAnalyzer
{
    public async ValueTask<TargetedSentimentResult> AnalyzeAsync(
        TargetedSentimentRequest request,
        CancellationToken cancellationToken)
    {
        var ruleBased = fantasySentimentAnalyzer.Analyze(request.Context);
        var model = await robertaSentimentAnalyzer.AnalyzeModelAsync(request, cancellationToken);

        var baseScore = model.IsAvailable ? model.Score : ruleBased.Score;
        var adjustment = model.IsAvailable
            ? CalculateFantasyAdjustment(ruleBased.Score)
            : 0;
        var finalScore = Math.Round(Math.Clamp(baseScore + adjustment, -1, 1), 3);

        return new TargetedSentimentResult(
            Math.Round(baseScore, 3),
            finalScore,
            TargetedSentimentResult.CategoryFromScore(finalScore),
            ruleBased.Score,
            model.IsAvailable ? model.Score : null,
            adjustment,
            nameof(HybridSentimentAnalyzer),
            request.Context,
            model.Diagnostics);
    }

    private static double CalculateFantasyAdjustment(double ruleScore)
    {
        if (Math.Abs(ruleScore) < 0.15)
        {
            return 0;
        }

        return Math.Round(Math.Clamp(ruleScore * 0.35, -0.35, 0.35), 3);
    }
}
