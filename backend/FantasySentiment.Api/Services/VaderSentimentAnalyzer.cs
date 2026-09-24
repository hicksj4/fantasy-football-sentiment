using VaderSharp2;

namespace FantasySentiment.Api.Services;

public sealed class VaderSentimentAnalyzer(
    FantasySentimentAnalyzer fantasySentimentAnalyzer) : ISentimentAnalyzer
{
    private readonly SentimentIntensityAnalyzer _vader = new();
    private readonly object _vaderLock = new();

    public ValueTask<TargetedSentimentResult> AnalyzeAsync(
        TargetedSentimentRequest request,
        CancellationToken cancellationToken)
    {
        var vader = ScoreVader(request.Context);
        var fantasy = fantasySentimentAnalyzer.Analyze(request.Context);
        var finalScore = BlendScores(vader.Compound, fantasy.Score);
        var fantasyAdjustment = Math.Round(finalScore - vader.Compound, 3);
        var diagnostics =
            $"vader={vader.Compound:0.###}; pos={vader.Positive:0.###}; neu={vader.Neutral:0.###}; neg={vader.Negative:0.###}; fantasyRule={fantasy.Score:0.###}; adjustment={fantasyAdjustment:0.###}";

        return ValueTask.FromResult(new TargetedSentimentResult(
            Math.Round(vader.Compound, 3),
            finalScore,
            TargetedSentimentResult.CategoryFromScore(finalScore),
            fantasy.Score,
            vader.Compound,
            fantasyAdjustment,
            nameof(VaderSentimentAnalyzer),
            request.Context,
            diagnostics));
    }

    private VaderScore ScoreVader(string context)
    {
        lock (_vaderLock)
        {
            var scores = _vader.PolarityScores(context);
            return new VaderScore(
                Math.Round(scores.Compound, 3),
                Math.Round(scores.Positive, 3),
                Math.Round(scores.Neutral, 3),
                Math.Round(scores.Negative, 3));
        }
    }

    private static double BlendScores(double vaderScore, double fantasyScore)
    {
        if (Math.Abs(fantasyScore) < 0.15)
        {
            return Math.Round(Math.Clamp(vaderScore, -1, 1), 3);
        }

        var signsDisagree = Math.Sign(vaderScore) != 0
                            && Math.Sign(vaderScore) != Math.Sign(fantasyScore);
        var vaderIsNeutral = Math.Abs(vaderScore) < 0.15;

        var blended = signsDisagree || vaderIsNeutral
            ? fantasyScore * 1.75 + vaderScore * 0.25
            : vaderScore * 0.75 + fantasyScore * 0.45;

        return Math.Round(Math.Clamp(blended, -1, 1), 3);
    }

    private sealed record VaderScore(
        double Compound,
        double Positive,
        double Neutral,
        double Negative);
}
