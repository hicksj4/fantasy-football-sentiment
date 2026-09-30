using System.Text.Json;
using Xunit.Abstractions;

namespace FantasySentiment.Tests.Benchmark;

public sealed class SentimentBenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task FullLabeledCorpus_GeneratesCanonicalBenchmarkReport()
    {
        var corpus = BenchmarkFixtureLoader.LoadCorpus();
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

        Assert.Contains(report.ByThread, thread => thread.DatasetSplit == "Development");
        Assert.Contains(report.ByThread, thread => thread.DatasetSplit == "Validation");
        Assert.Contains(report.ByThread, thread => thread.DatasetSplit == "EvaluationB");
        Assert.True(report.TotalComments > 0);
        Assert.True(report.TotalExpectedTargets > 0);
        Assert.True(report.TargetMetrics.TruePositive + report.TargetMetrics.FalseNegative > 0);
        Assert.True(report.FantasyOutlookMetrics.Total > 0);
        Assert.Contains(report.SplitPrecisionSummaries, summary => summary.DatasetSplit == "Development");
        Assert.Contains(report.SplitPrecisionSummaries, summary => summary.DatasetSplit == "Validation");
        Assert.Contains(report.SplitPrecisionSummaries, summary => summary.DatasetSplit == "EvaluationB");
        Assert.Contains("Scope: canonical full labeled model-health report.", await File.ReadAllTextAsync(markdownPath));
        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(markdownPath));
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task EvaluationBOnly_GeneratesEvaluationBReport()
    {
        var corpus = BenchmarkFixtureLoader.LoadCorpus("EvaluationB");
        var runner = new SentimentBenchmarkRunner(corpus);

        var report = await runner.RunAsync();
        var benchmarkDirectory = Path.Combine(corpus.RepositoryRoot, "artifacts", "benchmark");
        Directory.CreateDirectory(benchmarkDirectory);

        var jsonPath = Path.Combine(benchmarkDirectory, "sentiment-evaluation-b.json");
        var markdownPath = Path.Combine(benchmarkDirectory, "sentiment-evaluation-b.md");
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(report, BenchmarkFixtureLoader.SerializerOptions));
        var markdown = runner.ToEvaluationBMarkdown(report);
        await File.WriteAllTextAsync(markdownPath, markdown);

        output.WriteLine(markdown);

        Assert.All(report.ByThread, thread => Assert.Equal("EvaluationB", thread.DatasetSplit));
        Assert.Contains(report.ByThread, thread =>
            thread.DatasetSplit == "EvaluationB"
            && thread.ThreadName == "OFFICIAL WEEK 1 SUNDAY AFTERNOON GAME THREAD");
        Assert.Contains("DatasetSplit: EvaluationB", markdown);
        Assert.Contains("formerly audited Holdout B", markdown);
        Assert.DoesNotContain("DatasetSplit: Development", markdown);
        Assert.DoesNotContain("DatasetSplit: Validation", markdown);
        Assert.Contains("fantasy-semantics-v5", markdown);
        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(markdownPath));
    }
}
