using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using HfTokenizer = Tokenizers.HuggingFace.Tokenizer.Tokenizer;

namespace FantasySentiment.Api.Services;

public sealed class RobertaOnnxSentimentModel : IDisposable
{
    private const int FallbackMaxSequenceLength = 512;
    private readonly HfTokenizer _tokenizer;
    private readonly InferenceSession _session;
    private readonly object _tokenizerLock = new();

    public RobertaOnnxSentimentModel(
        string modelPath,
        string tokenizerPath,
        string? configPath = null,
        string? tokenizerConfigPath = null,
        int? configuredMaxSequenceLength = null)
    {
        ModelPath = modelPath;
        TokenizerPath = tokenizerPath;
        ConfigPath = configPath;
        TokenizerConfigPath = tokenizerConfigPath;
        MaxSequenceLength = ResolveMaxSequenceLength(configPath, tokenizerConfigPath, configuredMaxSequenceLength);

        _tokenizer = HfTokenizer.FromFile(tokenizerPath);
        _session = new InferenceSession(modelPath);
        InputNames = _session.InputMetadata.Keys.ToArray();
        OutputNames = _session.OutputMetadata.Keys.ToArray();
    }

    public string ModelPath { get; }
    public string TokenizerPath { get; }
    public string? ConfigPath { get; }
    public string? TokenizerConfigPath { get; }
    public int MaxSequenceLength { get; }
    public IReadOnlyList<string> InputNames { get; }
    public IReadOnlyList<string> OutputNames { get; }

    public RobertaTokenization Tokenize(string context, string target)
    {
        var modelInput = BuildModelInput(context, target);
        var encoding = Encode(modelInput);
        if (encoding.Ids.Count <= MaxSequenceLength)
        {
            return ToTokenization(modelInput, encoding);
        }

        var truncatedContext = TruncateContextToFit(context, target);
        var truncatedInput = BuildModelInput(truncatedContext, target);
        return ToTokenization(truncatedInput, Encode(truncatedInput));
    }

    public RobertaInferenceResult Analyze(string context, string target)
    {
        var tokenization = Tokenize(context, target);
        var inputs = BuildInputs(tokenization);
        using var results = _session.Run(inputs);
        var logits = results.First().AsTensor<float>().ToArray();
        var score = RobertaSentimentScoreMapper.Map(logits);

        return new RobertaInferenceResult(
            score.Score,
            score.WinningLabel,
            score.WinningProbability,
            score.Probabilities,
            tokenization.SequenceLength,
            tokenization.ModelInput,
            tokenization.InputIds,
            tokenization.AttentionMask,
            tokenization.TypeIds);
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    private Messages.Tokenizer.Encoding Encode(string modelInput)
    {
        lock (_tokenizerLock)
        {
            return _tokenizer
                .Encode(
                    modelInput,
                    add_special_tokens: true,
                    include_type_ids: true,
                    include_tokens: true,
                    include_special_tokens_mask: true,
                    include_attention_mask: true)
                .Encodings[0];
        }
    }

    private List<NamedOnnxValue> BuildInputs(RobertaTokenization tokenization)
    {
        var inputs = new List<NamedOnnxValue>();
        foreach (var inputName in InputNames)
        {
            var lower = inputName.ToLowerInvariant();
            if (lower == "input_ids")
            {
                inputs.Add(ToNamedTensor(inputName, tokenization.InputIds));
            }
            else if (lower == "attention_mask")
            {
                inputs.Add(ToNamedTensor(inputName, tokenization.AttentionMask));
            }
            else if (lower == "token_type_ids")
            {
                inputs.Add(ToNamedTensor(inputName, tokenization.TypeIds));
            }
            else
            {
                throw new InvalidOperationException($"Unsupported RoBERTa ONNX input '{inputName}'.");
            }
        }

        return inputs;
    }

    private static NamedOnnxValue ToNamedTensor(string name, IReadOnlyList<long> values)
    {
        var tensor = new DenseTensor<long>(values.ToArray(), [1, values.Count]);
        return NamedOnnxValue.CreateFromTensor(name, tensor);
    }

    private string TruncateContextToFit(string context, string target)
    {
        var normalizedContext = context.Trim();
        if (normalizedContext.Length == 0)
        {
            return normalizedContext;
        }

        var low = 0;
        var high = normalizedContext.Length;
        var best = normalizedContext;

        while (low <= high)
        {
            var mid = (low + high) / 2;
            var candidate = normalizedContext[..mid].Trim();
            var sequenceLength = Encode(BuildModelInput(candidate, target)).Ids.Count;
            if (sequenceLength <= MaxSequenceLength)
            {
                best = candidate;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best;
    }

    private static RobertaTokenization ToTokenization(
        string modelInput,
        Messages.Tokenizer.Encoding encoding)
    {
        return new RobertaTokenization(
            modelInput,
            encoding.Ids.Select(id => (long)id).ToArray(),
            encoding.AttentionMask.Select(id => (long)id).ToArray(),
            encoding.TypeIds.Select(id => (long)id).ToArray(),
            encoding.Tokens.ToArray());
    }

    public static string BuildModelInput(string context, string target)
    {
        return $"{context.Trim()} </s> {target.Trim()}".Trim();
    }

    private static int ResolveMaxSequenceLength(
        string? configPath,
        string? tokenizerConfigPath,
        int? configuredMaxSequenceLength)
    {
        if (configuredMaxSequenceLength is > 0)
        {
            return configuredMaxSequenceLength.Value;
        }

        var tokenizerMax = ReadJsonInt(tokenizerConfigPath, "model_max_length");
        if (tokenizerMax is > 0 and < 100_000)
        {
            return tokenizerMax.Value;
        }

        var maxPositionEmbeddings = ReadJsonInt(configPath, "max_position_embeddings");
        if (maxPositionEmbeddings is > 4)
        {
            return maxPositionEmbeddings.Value - 2;
        }

        return FallbackMaxSequenceLength;
    }

    private static int? ReadJsonInt(string? path, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetInt32(out var value)
            ? value
            : null;
    }
}

public sealed record RobertaTokenization(
    string ModelInput,
    IReadOnlyList<long> InputIds,
    IReadOnlyList<long> AttentionMask,
    IReadOnlyList<long> TypeIds,
    IReadOnlyList<string> Tokens)
{
    public int SequenceLength => InputIds.Count;
}

public sealed record RobertaInferenceResult(
    double Score,
    string WinningLabel,
    double WinningProbability,
    IReadOnlyDictionary<string, double> Probabilities,
    int SequenceLength,
    string ModelInput,
    IReadOnlyList<long> InputIds,
    IReadOnlyList<long> AttentionMask,
    IReadOnlyList<long> TypeIds);
