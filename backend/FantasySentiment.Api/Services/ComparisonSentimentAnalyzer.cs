namespace FantasySentiment.Api.Services;

public sealed class ComparisonSentimentAnalyzer(
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
        var adjustment = model.IsAvailable ? Math.Round(Math.Clamp(ruleBased.Score * 0.35, -0.35, 0.35), 3) : 0;
        var finalScore = Math.Round(Math.Clamp(baseScore + adjustment, -1, 1), 3);
        var diagnostics = $"rule={ruleBased.Score:0.###}; roberta={(model.IsAvailable ? model.Score.ToString("0.###") : "unavailable")}; adjustment={adjustment:0.###}; {model.Diagnostics}";

        return new TargetedSentimentResult(
            Math.Round(baseScore, 3),
            finalScore,
            TargetedSentimentResult.CategoryFromScore(finalScore),
            ruleBased.Score,
            model.IsAvailable ? model.Score : null,
            adjustment,
            nameof(ComparisonSentimentAnalyzer),
            request.Context,
            diagnostics);
    }
}
