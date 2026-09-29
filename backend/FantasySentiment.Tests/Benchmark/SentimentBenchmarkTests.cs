using System.Text.Json;
using Xunit.Abstractions;

namespace FantasySentiment.Tests.Benchmark;

public sealed class SentimentBenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task DevelopmentFixtures_RunAgainstCurrentProductionPipeline()
    {
        var corpus = BenchmarkFixtureLoader.LoadCorpus("Development");
        var runner = new SentimentBenchmarkRunner(corpus);

        var report = await runner.RunAsync();
        var benchmarkDirectory = Path.Combine(corpus.RepositoryRoot, "artifacts", "benchmark");
        Directory.CreateDirectory(benchmarkDirectory);

        var jsonPath = Path.Combine(benchmarkDirectory, "sentiment-benchmark.json");
        var markdownPath = Path.Combine(benchmarkDirectory, "sentiment-benchmark.md");
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(report, BenchmarkFixtureLoader.SerializerOptions));
        await File.WriteAllTextAsync(markdownPath, runner.ToMarkdown(report));

        output.WriteLine(runner.ToMarkdown(report));

        Assert.Equal(3, report.FixtureCount);
        Assert.True(report.TotalComments > 0);
        Assert.True(report.TotalExpectedTargets > 0);
        Assert.True(report.TargetMetrics.TruePositive + report.TargetMetrics.FalseNegative > 0);
        Assert.True(report.FantasyOutlookMetrics.Total > 0);
        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(markdownPath));
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task DevelopmentAndValidationFixtures_ReportDatasetSplits()
    {
        var corpus = BenchmarkFixtureLoader.LoadCorpus();
        var runner = new SentimentBenchmarkRunner(corpus);

        var report = await runner.RunAsync();
        var benchmarkDirectory = Path.Combine(corpus.RepositoryRoot, "artifacts", "benchmark");
        Directory.CreateDirectory(benchmarkDirectory);

        var jsonPath = Path.Combine(benchmarkDirectory, "sentiment-validation.json");
        var markdownPath = Path.Combine(benchmarkDirectory, "sentiment-validation.md");
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(report, BenchmarkFixtureLoader.SerializerOptions));
        var markdown = runner.ToMarkdown(report);
        await File.WriteAllTextAsync(markdownPath, markdown);

        output.WriteLine(markdown);

        Assert.Contains(report.ByThread, thread => thread.DatasetSplit == "Development");
        Assert.Contains(report.ByThread, thread => thread.DatasetSplit == "Validation");
        Assert.Contains("## Dataset Split Summary", markdown);
        Assert.Contains("## Validation Fantasy Outlook Confusion Matrix", markdown);
        Assert.Contains("## Drake London Validation Trajectory", markdown);
        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(markdownPath));
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task AuditedHoldoutBFixture_GeneratesFrozenV4GeneralizationReport()
    {
        var corpus = BenchmarkFixtureLoader.LoadCorpus();
        var runner = new SentimentBenchmarkRunner(corpus);

        var report = await runner.RunAsync();
        var benchmarkDirectory = Path.Combine(corpus.RepositoryRoot, "artifacts", "benchmark");
        Directory.CreateDirectory(benchmarkDirectory);

        var jsonPath = Path.Combine(benchmarkDirectory, "sentiment-holdout-b-audited.json");
        var markdownPath = Path.Combine(benchmarkDirectory, "sentiment-holdout-b-audited.md");
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(report, BenchmarkFixtureLoader.SerializerOptions));

        var markdown = string.Join(
            Environment.NewLine,
            [
                "# Holdout B -- Audited Ground Truth",
                string.Empty,
                "DatasetSplit: Holdout  ",
                "Semantic profile: fantasy-semantics-v4  ",
                "Analyzer: frozen  ",
                "Ground truth: Audited  ",
                "Source: Week 1 Sunday Afternoon Game Thread",
                string.Empty,
                runner.ToMarkdown(report)
            ]);
        await File.WriteAllTextAsync(markdownPath, markdown);

        output.WriteLine(markdown);

        Assert.Contains(report.ByThread, thread =>
            thread.DatasetSplit == "Holdout"
            && thread.ThreadName == "OFFICIAL WEEK 1 SUNDAY AFTERNOON GAME THREAD");
        Assert.Contains("Ground truth: Audited", markdown);
        Assert.Contains("fantasy-semantics-v4", markdown);
        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(markdownPath));
    }
}
