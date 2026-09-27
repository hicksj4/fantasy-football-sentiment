namespace FantasySentiment.Api.Services;

public sealed class RuleBasedFantasySentimentAnalyzer(
    FantasySentimentAnalyzer fantasySentimentAnalyzer) : ISentimentAnalyzer
{
    public ValueTask<TargetedSentimentResult> AnalyzeAsync(
        TargetedSentimentRequest request,
        CancellationToken cancellationToken)
    {
        var targetText = string.IsNullOrWhiteSpace(request.TargetText)
            ? request.Context
            : request.TargetText;
        var preparation = fantasySentimentAnalyzer.PrepareForVader(targetText);
        var result = fantasySentimentAnalyzer.AnalyzeTarget(
            targetText,
            request.TargetPerspective,
            rawVaderScore: 0,
            preparation,
            request.Mention);
        var diagnostics =
            $"fantasy={result.FantasyAdjustment:0.###}; emoji={result.EmojiAdjustment:0.###}; structural={result.StructuralAdjustment:0.###}; perspective={result.PerspectiveAdjustment:0.###}; question={result.QuestionAdjustment:0.###}; semanticConfidence={result.SemanticConfidence:0.###}; rules={string.Join('|', result.AppliedRules)}";

        return ValueTask.FromResult(new TargetedSentimentResult(
            0,
            result.FinalScore,
            TargetedSentimentResult.CategoryFromScore(result.FinalScore),
            Math.Round(
                result.FantasyAdjustment
                + result.EmojiAdjustment
                + result.StructuralAdjustment
                + result.PerspectiveAdjustment
                + result.QuestionAdjustment,
                3),
            null,
            result.FinalScore,
            nameof(RuleBasedFantasySentimentAnalyzer),
            request.Context,
            diagnostics));
    }
}
