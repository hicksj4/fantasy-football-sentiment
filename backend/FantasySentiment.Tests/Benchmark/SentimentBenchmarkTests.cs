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
    public async Task DevelopmentAndHoldoutFixtures_ReportDatasetSplits()
    {
        var corpus = BenchmarkFixtureLoader.LoadCorpus();
        var runner = new SentimentBenchmarkRunner(corpus);

        var report = await runner.RunAsync();
        var benchmarkDirectory = Path.Combine(corpus.RepositoryRoot, "artifacts", "benchmark");
        Directory.CreateDirectory(benchmarkDirectory);

        var jsonPath = Path.Combine(benchmarkDirectory, "sentiment-holdout.json");
        var markdownPath = Path.Combine(benchmarkDirectory, "sentiment-holdout.md");
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(report, BenchmarkFixtureLoader.SerializerOptions));
        var markdown = runner.ToMarkdown(report);
        await File.WriteAllTextAsync(markdownPath, markdown);

        output.WriteLine(markdown);

        Assert.Contains(report.ByThread, thread => thread.DatasetSplit == "Development");
        Assert.Contains(report.ByThread, thread => thread.DatasetSplit == "Holdout");
        Assert.Contains("## Dataset Split Summary", markdown);
        Assert.Contains("## Holdout Fantasy Outlook Confusion Matrix", markdown);
        Assert.Contains("## Drake London Holdout Trajectory", markdown);
        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(markdownPath));
    }
}
