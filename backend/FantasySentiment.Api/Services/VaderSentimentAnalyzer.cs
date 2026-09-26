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
        var analysis = AnalyzeDetailed(request);
        var semanticRuleScore = Math.Round(
            analysis.FantasyAdjustment
            + analysis.EmojiAdjustment
            + analysis.PerspectiveAdjustment
            + analysis.QuestionAdjustment,
            3);

        return ValueTask.FromResult(new TargetedSentimentResult(
            analysis.RawVaderScore,
            analysis.FinalScore,
            TargetedSentimentResult.CategoryFromScore(analysis.FinalScore),
            semanticRuleScore,
            analysis.RawVaderScore,
            analysis.TotalAdjustment,
            nameof(VaderSentimentAnalyzer),
            request.Context,
            analysis.ToDiagnostics()));
    }

    public SentimentAnalysisResult AnalyzeDetailed(TargetedSentimentRequest request)
    {
        var targetText = ResolveTargetText(request);
        var preparation = fantasySentimentAnalyzer.PrepareForVader(targetText);
        var vader = ScoreVader(preparation.VaderText);
        var semantic = fantasySentimentAnalyzer.AnalyzeTarget(
            targetText,
            request.TargetPerspective,
            vader.Compound,
            preparation);

        return new SentimentAnalysisResult(
            targetText,
            request.TargetPerspective,
            preparation.VaderText,
            Math.Round(vader.Compound, 3),
            semantic.FantasyAdjustment,
            semantic.EmojiAdjustment,
            semantic.PerspectiveAdjustment,
            semantic.QuestionAdjustment,
            semantic.FinalScore,
            semantic.MatchedPositiveTerms,
            semantic.MatchedNegativeTerms,
            semantic.MatchedPhrases,
            semantic.MatchedEmoji,
            semantic.AppliedRules);
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

    private static string ResolveTargetText(TargetedSentimentRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.TargetText))
        {
            return request.TargetText;
        }

        var targetContext = request.Context
            .Split(Environment.NewLine, StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith("Target context:", StringComparison.OrdinalIgnoreCase));
        if (targetContext is not null)
        {
            return targetContext["Target context:".Length..].Trim();
        }

        return request.Context;
    }

    private sealed record VaderScore(
        double Compound,
        double Positive,
        double Neutral,
        double Negative);
}
