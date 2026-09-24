using System.Diagnostics;
using System.Text.Json;
using FantasySentiment.Api.Services;

var options = ParseArgs(args);
var examples = JsonSerializer.Deserialize<List<ParityExample>>(
    File.ReadAllText(options.ExamplesPath),
    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];

using var model = new RobertaOnnxSentimentModel(
    options.ModelPath,
    options.TokenizerPath,
    options.ConfigPath,
    options.TokenizerConfigPath,
    options.MaxSequenceLength);

foreach (var example in examples)
{
    model.Analyze(example.Context, example.Target);
}

var stopwatch = Stopwatch.StartNew();
var outputs = new List<object>();
for (var repeat = 0; repeat < options.Repeat; repeat++)
{
    foreach (var example in examples)
    {
        var result = model.Analyze(example.Context, example.Target);
        if (repeat == 0)
        {
            outputs.Add(new
            {
                example.Id,
                example.Context,
                example.Target,
                result.ModelInput,
                inputIds = result.InputIds,
                attentionMask = result.AttentionMask,
                typeIds = result.TypeIds,
                result.SequenceLength,
                result.Probabilities,
                result.WinningLabel,
                result.Score
            });
        }
    }
}
stopwatch.Stop();

var analyses = Math.Max(1, options.Repeat) * examples.Count;
var payload = new
{
    runtime = "dotnet-onnxruntime",
    modelPath = options.ModelPath,
    tokenizerPath = options.TokenizerPath,
    model.MaxSequenceLength,
    model.InputNames,
    model.OutputNames,
    repeat = options.Repeat,
    elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
    analysesPerSecond = Math.Round(analyses / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds), 2),
    examples = outputs
};

Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
}));

static ParityOptions ParseArgs(string[] args)
{
    var root = FindRepoRoot();
    var assets = Path.Combine(root, "backend", "FantasySentiment.Api", "Models", "Sentiment");
    var options = new ParityOptions(
        Path.Combine(assets, "model.onnx"),
        Path.Combine(assets, "tokenizer.json"),
        Path.Combine(assets, "config.json"),
        Path.Combine(assets, "tokenizer_config.json"),
        Path.Combine(root, "tools", "sentiment-model", "examples.json"),
        null,
        1);

    for (var i = 0; i < args.Length; i++)
    {
        var name = args[i];
        var value = i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {name}.");
        options = name switch
        {
            "--model-path" => options with { ModelPath = Path.GetFullPath(value) },
            "--tokenizer-path" => options with { TokenizerPath = Path.GetFullPath(value) },
            "--config-path" => options with { ConfigPath = Path.GetFullPath(value) },
            "--tokenizer-config-path" => options with { TokenizerConfigPath = Path.GetFullPath(value) },
            "--examples" => options with { ExamplesPath = Path.GetFullPath(value) },
            "--max-sequence-length" => options with { MaxSequenceLength = int.Parse(value) },
            "--repeat" => options with { Repeat = int.Parse(value) },
            _ => throw new ArgumentException($"Unknown argument: {name}")
        };
    }

    return options;
}

static string FindRepoRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            var examplesPath = Path.Combine(directory.FullName, "tools", "sentiment-model", "examples.json");
            var apiProjectPath = Path.Combine(directory.FullName, "backend", "FantasySentiment.Api", "FantasySentiment.Api.csproj");
            if (File.Exists(examplesPath) && File.Exists(apiProjectPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
    }

    throw new DirectoryNotFoundException("Could not find repository root containing backend and sentiment tool files.");
}

internal sealed record ParityOptions(
    string ModelPath,
    string TokenizerPath,
    string ConfigPath,
    string TokenizerConfigPath,
    string ExamplesPath,
    int? MaxSequenceLength,
    int Repeat);

internal sealed record ParityExample(
    string Id,
    string Context,
    string Target);
