using FantasySentiment.Api.Options;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Services;

public sealed class RobertaSentimentAnalyzer : ISentimentAnalyzer, IDisposable
{
    private readonly SentimentOptions _options;
    private readonly RobertaOnnxSentimentModel? _model;

    public RobertaSentimentAnalyzer(
        IOptions<SentimentOptions> options,
        IWebHostEnvironment environment,
        ILogger<RobertaSentimentAnalyzer> logger)
    {
        _options = options.Value;
        var assets = ResolveAssets(_options.Roberta, environment.ContentRootPath);
        Status = RobertaRuntimeStatus.NotLoaded(_options.Roberta.Enabled, assets);

        if (!_options.Roberta.Enabled)
        {
            return;
        }

        var missing = assets.MissingRequiredAssets().ToArray();
        if (missing.Length > 0)
        {
            Status = RobertaRuntimeStatus.MissingAssets(assets, missing);
            return;
        }

        try
        {
            _model = new RobertaOnnxSentimentModel(
                assets.ModelPath,
                assets.TokenizerPath,
                assets.ConfigPath,
                assets.TokenizerConfigPath,
                _options.Roberta.MaxSequenceLength);

            Status = RobertaRuntimeStatus.LoadedStatus(assets, _model);
            logger.LogInformation(
                "Loaded RoBERTa sentiment model from {ModelPath}. Max sequence length: {MaxSequenceLength}.",
                assets.ModelPath,
                _model.MaxSequenceLength);
        }
        catch (Exception ex)
        {
            Status = RobertaRuntimeStatus.Failed(assets, ex.Message);
            logger.LogError(ex, "Failed to load RoBERTa sentiment model from {ModelPath}.", assets.ModelPath);
        }
    }

    public RobertaRuntimeStatus Status { get; private set; }

    public ValueTask<TargetedSentimentResult> AnalyzeAsync(
        TargetedSentimentRequest request,
        CancellationToken cancellationToken)
    {
        var model = AnalyzeModel(request);
        if (!model.IsAvailable)
        {
            throw new InvalidOperationException(model.Diagnostics);
        }

        return ValueTask.FromResult(new TargetedSentimentResult(
            model.Score,
            model.Score,
            TargetedSentimentResult.CategoryFromScore(model.Score),
            0,
            model.Score,
            0,
            nameof(RobertaSentimentAnalyzer),
            request.Context,
            model.Diagnostics));
    }

    public ValueTask<ModelSentimentResult> AnalyzeModelAsync(
        TargetedSentimentRequest request,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(AnalyzeModel(request));
    }

    public void EnsureUsableForMode(string mode)
    {
        if (IsRuleBasedMode(mode))
        {
            return;
        }

        if (!_options.Roberta.Enabled)
        {
            if (IsRobertaOnlyMode(mode))
            {
                throw new InvalidOperationException("Sentiment mode is Roberta, but Sentiment:Roberta:Enabled is false.");
            }

            return;
        }

        if (!Status.Loaded)
        {
            throw new InvalidOperationException(Status.ErrorMessage ?? "RoBERTa is enabled but not loaded.");
        }
    }

    public void Dispose()
    {
        _model?.Dispose();
    }

    private ModelSentimentResult AnalyzeModel(TargetedSentimentRequest request)
    {
        if (!_options.Roberta.Enabled)
        {
            return new ModelSentimentResult(
                false,
                0,
                nameof(RobertaSentimentAnalyzer),
                "RoBERTa is disabled. Set Sentiment:Roberta:Enabled=true and configure model assets to use it.");
        }

        if (_model is null)
        {
            return new ModelSentimentResult(
                false,
                0,
                nameof(RobertaSentimentAnalyzer),
                Status.ErrorMessage ?? "RoBERTa model assets are unavailable.");
        }

        var result = _model.Analyze(request.Context, request.Mention.PlayerName);
        var diagnostics = BuildDiagnostics(request, result);
        return new ModelSentimentResult(
            true,
            result.Score,
            nameof(RobertaSentimentAnalyzer),
            diagnostics,
            result.Probabilities,
            result.SequenceLength,
            result.ModelInput);
    }

    private static string BuildDiagnostics(
        TargetedSentimentRequest request,
        RobertaInferenceResult result)
    {
        var probabilities = string.Join(
            ", ",
            result.Probabilities.Select(pair => $"{pair.Key}={pair.Value:0.######}"));

        return $"target={request.Mention.PlayerName}; sequenceLength={result.SequenceLength}; winningLabel={result.WinningLabel}; winningProbability={result.WinningProbability:0.######}; probabilities=[{probabilities}]";
    }

    private static bool IsRuleBasedMode(string mode)
    {
        return mode.Equals(SentimentModes.RuleBased, StringComparison.OrdinalIgnoreCase)
               || mode.Equals("rule-based", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRobertaOnlyMode(string mode)
    {
        return mode.Equals(SentimentModes.Roberta, StringComparison.OrdinalIgnoreCase);
    }

    private static RobertaAssetPaths ResolveAssets(
        RobertaSentimentOptions options,
        string contentRootPath)
    {
        var defaultDirectory = Path.Combine(contentRootPath, "Models", "Sentiment");
        var modelPath = ResolvePath(
            options.ModelPath ?? options.OnnxModelPath,
            Path.Combine(defaultDirectory, "model.onnx"),
            contentRootPath);
        var tokenizerPath = ResolvePath(
            options.TokenizerPath,
            Path.Combine(defaultDirectory, "tokenizer.json"),
            contentRootPath);
        var configPath = ResolvePath(
            options.ConfigPath,
            Path.Combine(defaultDirectory, "config.json"),
            contentRootPath);
        var tokenizerConfigPath = ResolvePath(
            options.TokenizerConfigPath,
            Path.Combine(defaultDirectory, "tokenizer_config.json"),
            contentRootPath);

        return new RobertaAssetPaths(modelPath, tokenizerPath, configPath, tokenizerConfigPath);
    }

    private static string ResolvePath(string? configuredPath, string defaultPath, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(defaultPath);
        }

        return Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(contentRootPath, configuredPath));
    }
}

public sealed record RobertaAssetPaths(
    string ModelPath,
    string TokenizerPath,
    string ConfigPath,
    string TokenizerConfigPath)
{
    public IEnumerable<string> MissingRequiredAssets()
    {
        if (!File.Exists(ModelPath))
        {
            yield return ModelPath;
        }

        if (!File.Exists(TokenizerPath))
        {
            yield return TokenizerPath;
        }
    }
}

public sealed record RobertaRuntimeStatus(
    bool Enabled,
    bool Loaded,
    string ModelPath,
    string TokenizerPath,
    string ConfigPath,
    string TokenizerConfigPath,
    bool ModelExists,
    bool TokenizerExists,
    bool ConfigExists,
    bool TokenizerConfigExists,
    int? MaxSequenceLength,
    IReadOnlyList<string> InputNames,
    IReadOnlyList<string> OutputNames,
    string? ErrorMessage)
{
    public static RobertaRuntimeStatus NotLoaded(bool enabled, RobertaAssetPaths assets)
    {
        return new RobertaRuntimeStatus(
            enabled,
            false,
            assets.ModelPath,
            assets.TokenizerPath,
            assets.ConfigPath,
            assets.TokenizerConfigPath,
            File.Exists(assets.ModelPath),
            File.Exists(assets.TokenizerPath),
            File.Exists(assets.ConfigPath),
            File.Exists(assets.TokenizerConfigPath),
            null,
            [],
            [],
            enabled ? "RoBERTa is enabled but has not been loaded." : "RoBERTa is disabled.");
    }

    public static RobertaRuntimeStatus MissingAssets(RobertaAssetPaths assets, IReadOnlyList<string> missing)
    {
        return new RobertaRuntimeStatus(
            true,
            false,
            assets.ModelPath,
            assets.TokenizerPath,
            assets.ConfigPath,
            assets.TokenizerConfigPath,
            File.Exists(assets.ModelPath),
            File.Exists(assets.TokenizerPath),
            File.Exists(assets.ConfigPath),
            File.Exists(assets.TokenizerConfigPath),
            null,
            [],
            [],
            $"RoBERTa is enabled but required assets are missing: {string.Join(", ", missing.Select(Path.GetFileName))}.");
    }

    public static RobertaRuntimeStatus LoadedStatus(RobertaAssetPaths assets, RobertaOnnxSentimentModel model)
    {
        return new RobertaRuntimeStatus(
            true,
            true,
            assets.ModelPath,
            assets.TokenizerPath,
            assets.ConfigPath,
            assets.TokenizerConfigPath,
            true,
            true,
            File.Exists(assets.ConfigPath),
            File.Exists(assets.TokenizerConfigPath),
            model.MaxSequenceLength,
            model.InputNames,
            model.OutputNames,
            null);
    }

    public static RobertaRuntimeStatus Failed(RobertaAssetPaths assets, string errorMessage)
    {
        return new RobertaRuntimeStatus(
            true,
            false,
            assets.ModelPath,
            assets.TokenizerPath,
            assets.ConfigPath,
            assets.TokenizerConfigPath,
            File.Exists(assets.ModelPath),
            File.Exists(assets.TokenizerPath),
            File.Exists(assets.ConfigPath),
            File.Exists(assets.TokenizerConfigPath),
            null,
            [],
            [],
            errorMessage);
    }
}
