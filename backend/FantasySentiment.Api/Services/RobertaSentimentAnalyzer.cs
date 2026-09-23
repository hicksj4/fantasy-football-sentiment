using FantasySentiment.Api.Options;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Services;

public sealed class RobertaSentimentAnalyzer(IOptions<SentimentOptions> options) : ISentimentAnalyzer
{
    private readonly SentimentOptions _options = options.Value;

    public async ValueTask<TargetedSentimentResult> AnalyzeAsync(
        TargetedSentimentRequest request,
        CancellationToken cancellationToken)
    {
        var model = await AnalyzeModelAsync(request, cancellationToken);
        var score = model.IsAvailable ? model.Score : 0;

        return new TargetedSentimentResult(
            Math.Round(score, 3),
            Math.Round(score, 3),
            TargetedSentimentResult.CategoryFromScore(score),
            0,
            model.IsAvailable ? model.Score : null,
            0,
            nameof(RobertaSentimentAnalyzer),
            request.Context,
            model.Diagnostics);
    }

    public ValueTask<ModelSentimentResult> AnalyzeModelAsync(
        TargetedSentimentRequest request,
        CancellationToken cancellationToken)
    {
        var roberta = _options.Roberta;
        if (!roberta.Enabled
            || string.IsNullOrWhiteSpace(roberta.OnnxModelPath)
            || string.IsNullOrWhiteSpace(roberta.TokenizerPath)
            || !File.Exists(roberta.OnnxModelPath)
            || !File.Exists(roberta.TokenizerPath))
        {
            return ValueTask.FromResult(new ModelSentimentResult(
                false,
                0,
                nameof(RobertaSentimentAnalyzer),
                "RoBERTa is not configured. Expected exported ONNX model and tokenizer assets."));
        }

        return ValueTask.FromResult(new ModelSentimentResult(
            false,
            0,
            nameof(RobertaSentimentAnalyzer),
            "RoBERTa configuration was found, but ONNX inference is not wired in this MVP branch."));
    }
}
