using System.Text.Json.Serialization;
using FantasySentiment.Api.Domain;

namespace FantasySentiment.Tests.Benchmark;

internal sealed class BenchmarkFixture
{
    public BenchmarkThreadMetadata Thread { get; set; } = new();
    public List<BenchmarkPlayerSeed> Players { get; set; } = [];
    public List<BenchmarkReferenceResult> ReferenceResults { get; set; } = [];
    public List<BenchmarkComment> Comments { get; set; } = [];
}

internal sealed class BenchmarkThreadMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Subreddit { get; set; } = "fantasyfootball";
    public string DatasetSplit { get; set; } = "Development";
    public string? SourceFile { get; set; }
}

internal sealed class BenchmarkPlayerSeed
{
    public string Name { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public List<string> Aliases { get; set; } = [];
}

internal sealed class BenchmarkReferenceResult
{
    public string Player { get; set; } = string.Empty;
    public double FantasyPointsHalfPpr { get; set; }
    public int WeeklyOverallRank { get; set; }
    public string? Notes { get; set; }
}

internal sealed class BenchmarkComment
{
    public string Id { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public int RedditScore { get; set; }
    public string Body { get; set; } = string.Empty;
    public BenchmarkExpected Expected { get; set; } = new();
    public string DatasetSplit { get; set; } = "Development";
    public string? Notes { get; set; }
}

internal sealed class BenchmarkExpected
{
    public string ContextRequirement { get; set; } = BenchmarkContextRequirements.SelfContained;
    public List<BenchmarkExpectedTarget> Targets { get; set; } = [];
}

internal sealed class BenchmarkExpectedTarget
{
    public string Player { get; set; } = string.Empty;
    public string Sentiment { get; set; } = BenchmarkSentimentLabels.Unclear;
    public string PlayerEvaluation { get; set; } = BenchmarkFantasyLabels.Unknown;
    public string FantasyOutlook { get; set; } = BenchmarkFantasyLabels.Unknown;
    public string Perspective { get; set; } = TargetPerspectives.Unknown;
    public List<string> Intents { get; set; } = [];
    public List<string> Reasons { get; set; } = [];
    public bool IsDebatable { get; set; }
    public string? Notes { get; set; }
}

internal static class BenchmarkSentimentLabels
{
    public const string StrongNegative = "StrongNegative";
    public const string Negative = "Negative";
    public const string Neutral = "Neutral";
    public const string Positive = "Positive";
    public const string StrongPositive = "StrongPositive";
    public const string Unclear = "Unclear";
}

internal static class BenchmarkFantasyLabels
{
    public const string StrongNegative = "StrongNegative";
    public const string Negative = "Negative";
    public const string Neutral = "Neutral";
    public const string Positive = "Positive";
    public const string StrongPositive = "StrongPositive";
    public const string Unknown = "Unknown";
}

internal static class BenchmarkContextRequirements
{
    public const string SelfContained = "SelfContained";
    public const string ReplyContext = "ReplyContext";
    public const string GameEventContext = "GameEventContext";
    public const string Ambiguous = "Ambiguous";
}

internal sealed class BenchmarkCorpus
{
    public required string RepositoryRoot { get; init; }
    public required IReadOnlyList<BenchmarkFixture> Fixtures { get; init; }
    public required IReadOnlyList<Player> Players { get; init; }
    public required FantasySentiment.Api.Options.SentimentLexiconOptions SentimentLexicon { get; init; }
    public required FantasySentiment.Api.Options.IntentLexiconOptions IntentLexicon { get; init; }
}

internal sealed class BenchmarkReport
{
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string Analyzer { get; set; } = string.Empty;
    public string TargetResolver { get; set; } = string.Empty;
    public int FixtureCount { get; set; }
    public int TotalComments { get; set; }
    public int TotalExpectedTargets { get; set; }
    public int CommentsWithoutExpectedTargets { get; set; }
    public int SentimentExcludedByContext { get; set; }
    public int ScorableSentimentTargets => SentimentMetrics.Total;
    public Dictionary<string, int> ContextRequirementCounts { get; set; } = [];
    public TargetMetricSummary TargetMetrics { get; set; } = new();
    public ClassificationMetricSummary SentimentMetrics { get; set; } = new();
    public ClassificationMetricSummary PerspectiveMetrics { get; set; } = new();
    public ClassificationMetricSummary IntentMetrics { get; set; } = new();
    public Dictionary<string, Dictionary<string, int>> SentimentConfusionMatrix { get; set; } =
        BenchmarkDirections.EmptyConfusionMatrix();
    public List<ThreadBenchmarkSummary> ByThread { get; set; } = [];
    public Dictionary<string, int> FailureCategories { get; set; } = [];
    public Dictionary<string, int> PerspectiveFailureCategories { get; set; } = [];
    public Dictionary<string, int> IntentFailureCategories { get; set; } = [];
    public Dictionary<string, int> SentimentFailureCategories { get; set; } = [];
    public List<BenchmarkFailure> PerspectiveFailureExamples { get; set; } = [];
    public List<BenchmarkFailure> IntentFailureExamples { get; set; } = [];
    public List<BenchmarkFailure> SentimentFailureExamples { get; set; } = [];
    public List<BenchmarkFailure> DebatableLabels { get; set; } = [];
    public List<BenchmarkFailure> RepresentativeFailures { get; set; } = [];
}

internal sealed class ThreadBenchmarkSummary
{
    public string ThreadName { get; set; } = string.Empty;
    public int CommentCount { get; set; }
    public int ExpectedTargetCount { get; set; }
    public int SentimentExcludedByContext { get; set; }
    public Dictionary<string, int> ContextRequirementCounts { get; set; } = [];
    public TargetMetricSummary TargetMetrics { get; set; } = new();
    public ClassificationMetricSummary SentimentMetrics { get; set; } = new();
    public ClassificationMetricSummary PerspectiveMetrics { get; set; } = new();
    public ClassificationMetricSummary IntentMetrics { get; set; } = new();
    public Dictionary<string, Dictionary<string, int>> SentimentConfusionMatrix { get; set; } =
        BenchmarkDirections.EmptyConfusionMatrix();
}

internal sealed class TargetMetricSummary
{
    public int TruePositive { get; set; }
    public int FalsePositive { get; set; }
    public int FalseNegative { get; set; }

    public double Precision => Round(TruePositive + FalsePositive == 0
        ? 1
        : TruePositive / (double)(TruePositive + FalsePositive));

    public double Recall => Round(TruePositive + FalseNegative == 0
        ? 1
        : TruePositive / (double)(TruePositive + FalseNegative));

    public double F1 => Round(Precision + Recall == 0
        ? 0
        : 2 * Precision * Recall / (Precision + Recall));

    [JsonIgnore]
    public int TotalExpected => TruePositive + FalseNegative;

    public void Add(TargetMetricSummary other)
    {
        TruePositive += other.TruePositive;
        FalsePositive += other.FalsePositive;
        FalseNegative += other.FalseNegative;
    }

    private static double Round(double value)
    {
        return Math.Round(value, 3);
    }
}

internal sealed class ClassificationMetricSummary
{
    public int Correct { get; set; }
    public int Total { get; set; }
    public double Accuracy => Math.Round(Total == 0 ? 1 : Correct / (double)Total, 3);

    public void Add(bool correct)
    {
        Total++;
        if (correct)
        {
            Correct++;
        }
    }

    public void Add(ClassificationMetricSummary other)
    {
        Correct += other.Correct;
        Total += other.Total;
    }
}

internal sealed class BenchmarkFailure
{
    public string Category { get; set; } = string.Empty;
    public string ThreadName { get; set; } = string.Empty;
    public string CommentId { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Expected { get; set; } = string.Empty;
    public string Actual { get; set; } = string.Empty;
    public string? ContextRequirement { get; set; }
    public string? Notes { get; set; }
    public string? Diagnostics { get; set; }
}

internal sealed record ActualTargetResult(
    string Player,
    string SentimentDirection,
    double SentimentScore,
    string Perspective,
    string Intent,
    string AttributionMethod,
    double TargetConfidence,
    string TargetText,
    string? Diagnostics);

internal static class BenchmarkDirections
{
    public const string Negative = "Negative";
    public const string Neutral = "Neutral";
    public const string Positive = "Positive";

    public static string FromScore(double score)
    {
        return score switch
        {
            >= 0.15 => Positive,
            <= -0.15 => Negative,
            _ => Neutral
        };
    }

    public static string? FromExpectedLabel(string label)
    {
        return label switch
        {
            BenchmarkSentimentLabels.StrongNegative => Negative,
            BenchmarkSentimentLabels.Negative => Negative,
            BenchmarkSentimentLabels.Neutral => Neutral,
            BenchmarkSentimentLabels.Positive => Positive,
            BenchmarkSentimentLabels.StrongPositive => Positive,
            _ => null
        };
    }

    public static Dictionary<string, Dictionary<string, int>> EmptyConfusionMatrix()
    {
        var labels = new[] { Negative, Neutral, Positive };
        return labels.ToDictionary(
            expected => expected,
            _ => labels.ToDictionary(actual => actual, _ => 0, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }
}
