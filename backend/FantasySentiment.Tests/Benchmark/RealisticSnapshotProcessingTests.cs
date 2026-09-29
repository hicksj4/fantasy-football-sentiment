using System.Text.Json;
using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using FantasySentiment.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace FantasySentiment.Tests.Benchmark;

public sealed class RealisticSnapshotProcessingTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task WeekTwoRedditSample_ProcessesThroughSnapshotPipeline()
    {
        var corpus = BenchmarkFixtureLoader.LoadCorpus();
        var apiRoot = Path.Combine(corpus.RepositoryRoot, "backend", "FantasySentiment.Api");
        await using var db = CreateDb();
        await new DemoDataSeeder(db, new FakeEnvironment(apiRoot), NullLogger<DemoDataSeeder>.Instance)
            .SeedAsync(CancellationToken.None);

        var processor = new SnapshotProcessingService(
            db,
            CreateJsonSource(apiRoot),
            new CommentTargetResolver(
                new PlayerMentionDetector(),
                NullLogger<CommentTargetResolver>.Instance),
            new VaderSentimentAnalyzer(new FantasySentimentAnalyzer(new BenchmarkOptionsMonitor<SentimentLexiconOptions>(corpus.SentimentLexicon))),
            new TargetContextBuilder(new MentionContextExtractor()),
            new ThreadContextBuilder(),
            new FantasyIntentClassifier(new BenchmarkOptionsMonitor<IntentLexiconOptions>(corpus.IntentLexicon)),
            new VoteWeightCalculator(),
            new SentimentScoringPolicy(),
            NullLogger<SnapshotProcessingService>.Instance);

        await processor.ProcessAsync("2026-week2-reddit-sample", skipIfProcessed: false, CancellationToken.None);
        var processedSnapshot = await db.AnalysisSnapshots.SingleAsync(item => item.Key == "2026-week2-reddit-sample");
        var mentionCountAfterProcess = await db.PlayerMentions.CountAsync(mention => mention.SnapshotId == processedSnapshot.Id);
        await processor.ProcessAsync("2026-week2-reddit-sample", skipIfProcessed: true, CancellationToken.None);
        var mentionCountAfterIdempotentSkip = await db.PlayerMentions.CountAsync(mention => mention.SnapshotId == processedSnapshot.Id);

        var report = await BuildReportAsync(db, mentionCountAfterProcess, mentionCountAfterIdempotentSkip);
        var benchmarkDirectory = Path.Combine(corpus.RepositoryRoot, "artifacts", "benchmark");
        Directory.CreateDirectory(benchmarkDirectory);
        var jsonPath = Path.Combine(benchmarkDirectory, "realistic-snapshot-summary.json");
        var markdownPath = Path.Combine(benchmarkDirectory, "realistic-snapshot-summary.md");
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(report, BenchmarkFixtureLoader.SerializerOptions));
        await File.WriteAllTextAsync(markdownPath, ToMarkdown(report));

        output.WriteLine(ToMarkdown(report));

        Assert.Equal(3, report.Posts);
        Assert.Equal(333, report.Comments);
        Assert.True(report.PlayerMentions > 0);
        Assert.True(report.UniquePlayers > 0);
        Assert.True(report.IdempotenceMentionCountStable);
        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(markdownPath));
    }

    private static async Task<RealisticSnapshotReport> BuildReportAsync(
        FantasySentimentDbContext db,
        int mentionCountAfterProcess,
        int mentionCountAfterIdempotentSkip)
    {
        var snapshot = await db.AnalysisSnapshots.SingleAsync(item => item.Key == "2026-week2-reddit-sample");
        var aggregates = await db.SnapshotPlayerAggregates
            .AsNoTracking()
            .Include(aggregate => aggregate.Player)
            .Where(aggregate => aggregate.SnapshotId == snapshot.Id)
            .ToListAsync();

        var mentions = await db.PlayerMentions
            .AsNoTracking()
            .Include(mention => mention.Player)
            .Include(mention => mention.Comment)
            .ThenInclude(comment => comment.Post)
            .Where(mention => mention.SnapshotId == snapshot.Id)
            .ToListAsync();

        return new RealisticSnapshotReport
        {
            SnapshotKey = snapshot.Key,
            Posts = snapshot.TotalPosts,
            Comments = snapshot.TotalComments,
            PlayerMentions = snapshot.TotalPlayerMentions,
            UniquePlayers = snapshot.UniquePlayersMentioned,
            IdempotenceMentionCountAfterProcess = mentionCountAfterProcess,
            IdempotenceMentionCountAfterSkip = mentionCountAfterIdempotentSkip,
            IdempotenceMentionCountStable = mentionCountAfterProcess == mentionCountAfterIdempotentSkip,
            GateExclusions = BuildGateExclusions(mentions),
            TopCommunitySentiment = aggregates
                .OrderByDescending(aggregate => aggregate.CommunitySentimentScore)
                .ThenByDescending(aggregate => aggregate.MentionCount)
                .Take(8)
                .Select(ToAggregateSummary)
                .ToList(),
            BottomCommunitySentiment = aggregates
                .OrderBy(aggregate => aggregate.CommunitySentimentScore)
                .ThenByDescending(aggregate => aggregate.MentionCount)
                .Take(8)
                .Select(ToAggregateSummary)
                .ToList(),
            TopDiscussionImpact = aggregates
                .OrderByDescending(aggregate => aggregate.ImpactScore)
                .ThenByDescending(aggregate => aggregate.MentionCount)
                .Take(8)
                .Select(ToAggregateSummary)
                .ToList(),
            TopPositiveImpact = aggregates
                .OrderByDescending(aggregate => aggregate.PositiveImpact)
                .ThenByDescending(aggregate => aggregate.MentionCount)
                .Take(8)
                .Select(ToAggregateSummary)
                .ToList(),
            TopNegativeImpact = aggregates
                .OrderBy(aggregate => aggregate.NegativeImpact)
                .ThenByDescending(aggregate => aggregate.MentionCount)
                .Take(8)
                .Select(ToAggregateSummary)
                .ToList(),
            MostInfluentialComments = mentions
                .Where(mention => mention.AnalysisDisposition == AnalysisDispositions.Scored)
                .GroupBy(mention => mention.PlayerId)
                .SelectMany(group =>
                {
                    var totalWeight = Math.Max(group.Sum(mention => mention.VoteWeight), 0.001);
                    return group.Select(mention => new InfluentialCommentSummary
                    {
                        Player = mention.Player.Name,
                        CommentExcerpt = Trim(mention.Comment.Body, 160),
                        RedditScore = mention.RedditScore,
                        Sentiment = mention.FinalSentimentScore,
                        VoteWeight = mention.VoteWeight,
                        Contribution = Math.Round((mention.FinalSentimentScore * mention.VoteWeight) / totalWeight, 3),
                        Thread = mention.Comment.Post.Title
                    });
                })
                .OrderByDescending(comment => Math.Abs(comment.Contribution))
                .Take(12)
                .ToList()
        };
    }

    private static GateExclusionSummary BuildGateExclusions(IReadOnlyList<PlayerMention> mentions)
    {
        var summary = new GateExclusionSummary
        {
            ActualScorableMentions = mentions.Count
        };

        foreach (var mention in mentions)
        {
            var semanticFailed = mention.SemanticConfidence < SentimentScoringPolicy.MinimumSemanticConfidence;
            var targetFailed = mention.TargetConfidence < SentimentScoringPolicy.MinimumTargetConfidence;
            if (!semanticFailed && !targetFailed)
            {
                summary.Scored++;
            }
            else if (semanticFailed && targetFailed)
            {
                summary.FailedBoth++;
            }
            else if (semanticFailed)
            {
                summary.SemanticConfidenceOnly++;
            }
            else
            {
                summary.TargetConfidenceOnly++;
            }
        }

        return summary;
    }

    private static AggregateSummary ToAggregateSummary(SnapshotPlayerAggregate aggregate)
    {
        return new AggregateSummary
        {
            Player = aggregate.Player.Name,
            Team = aggregate.Player.Team,
            Position = aggregate.Player.Position,
            Mentions = aggregate.MentionCount,
            ScoredMentions = aggregate.ScoredMentionCount,
            MentionOnlyMentions = aggregate.MentionOnlyCount,
            SentimentCoverage = aggregate.SentimentCoverage,
            HasScoredSentiment = aggregate.HasScoredSentiment,
            CommunitySentiment = aggregate.CommunitySentimentScore,
            Sentiment = aggregate.SentimentScore,
            Impact = aggregate.ImpactScore,
            PositiveImpact = aggregate.PositiveImpact,
            NegativeImpact = aggregate.NegativeImpact,
            Confidence = aggregate.Confidence
        };
    }

    private static string ToMarkdown(RealisticSnapshotReport report)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# Realistic Snapshot Processing Summary");
        builder.AppendLine();
        builder.AppendLine($"Snapshot: {report.SnapshotKey}");
        builder.AppendLine($"Posts: {report.Posts}");
        builder.AppendLine($"Comments: {report.Comments}");
        builder.AppendLine($"Player mentions: {report.PlayerMentions}");
        builder.AppendLine($"Unique players: {report.UniquePlayers}");
        builder.AppendLine($"Idempotence check: mentions after process={report.IdempotenceMentionCountAfterProcess}, after skip={report.IdempotenceMentionCountAfterSkip}, stable={report.IdempotenceMentionCountStable}");
        builder.AppendLine();
        builder.AppendLine("## Production Gate Exclusions");
        builder.AppendLine();
        builder.AppendLine($"- Actual scorable mentions: {report.GateExclusions.ActualScorableMentions}");
        builder.AppendLine($"- Scored: {report.GateExclusions.Scored}");
        builder.AppendLine($"- Semantic confidence only: {report.GateExclusions.SemanticConfidenceOnly}");
        builder.AppendLine($"- Target confidence only: {report.GateExclusions.TargetConfidenceOnly}");
        builder.AppendLine($"- Failed both: {report.GateExclusions.FailedBoth}");
        AppendAggregateSection(builder, "Top Community Sentiment", report.TopCommunitySentiment);
        AppendAggregateSection(builder, "Bottom Community Sentiment", report.BottomCommunitySentiment);
        AppendAggregateSection(builder, "Top Discussion Impact", report.TopDiscussionImpact);
        AppendAggregateSection(builder, "Top Positive Impact", report.TopPositiveImpact);
        AppendAggregateSection(builder, "Top Negative Impact", report.TopNegativeImpact);
        builder.AppendLine();
        builder.AppendLine("## Most Influential Comments");
        builder.AppendLine();
        foreach (var comment in report.MostInfluentialComments)
        {
            builder.AppendLine($"- {comment.Player}: {comment.Contribution:+0.###;-0.###;0} contribution, sentiment {comment.Sentiment:+0.###;-0.###;0}, score {comment.RedditScore}, weight {comment.VoteWeight:0.###}");
            builder.AppendLine($"  - \"{comment.CommentExcerpt}\"");
            builder.AppendLine($"  - Thread: {comment.Thread}");
        }

        return builder.ToString();
    }

    private static void AppendAggregateSection(
        System.Text.StringBuilder builder,
        string title,
        IReadOnlyList<AggregateSummary> rows)
    {
        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        foreach (var row in rows)
        {
            builder.AppendLine($"- {row.Player} ({row.Team} {row.Position}): mentions={row.Mentions}, scored={row.ScoredMentions}, coverage={row.SentimentCoverage:0.###}, community={row.CommunitySentiment:+0.###;-0.###;0}, impact={row.Impact:0.###}, positive={row.PositiveImpact:0.###}, negative={row.NegativeImpact:0.###}, confidence={row.Confidence}");
        }
    }

    private static FantasySentimentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<FantasySentimentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FantasySentimentDbContext(options);
    }

    private static JsonSnapshotSource CreateJsonSource(string apiRoot)
    {
        return new JsonSnapshotSource(
            new FakeEnvironment(apiRoot),
            Options.Create(new SnapshotProcessingOptions
            {
                FixtureDirectory = "SeedData/Snapshots"
            }),
            NullLogger<JsonSnapshotSource>.Instance);
    }

    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : $"{value[..Math.Max(0, maxLength - 3)]}...";
    }

    private sealed class BenchmarkOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name)
        {
            return value;
        }

        public IDisposable? OnChange(Action<T, string?> listener)
        {
            return null;
        }
    }

    private sealed class FakeEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RealisticSnapshotReport
    {
        public string SnapshotKey { get; set; } = string.Empty;
        public int Posts { get; set; }
        public int Comments { get; set; }
        public int PlayerMentions { get; set; }
        public int UniquePlayers { get; set; }
        public int IdempotenceMentionCountAfterProcess { get; set; }
        public int IdempotenceMentionCountAfterSkip { get; set; }
        public bool IdempotenceMentionCountStable { get; set; }
        public GateExclusionSummary GateExclusions { get; set; } = new();
        public List<AggregateSummary> TopCommunitySentiment { get; set; } = [];
        public List<AggregateSummary> BottomCommunitySentiment { get; set; } = [];
        public List<AggregateSummary> TopDiscussionImpact { get; set; } = [];
        public List<AggregateSummary> TopPositiveImpact { get; set; } = [];
        public List<AggregateSummary> TopNegativeImpact { get; set; } = [];
        public List<InfluentialCommentSummary> MostInfluentialComments { get; set; } = [];
    }

    private sealed class AggregateSummary
    {
        public string Player { get; set; } = string.Empty;
        public string Team { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public int Mentions { get; set; }
        public int ScoredMentions { get; set; }
        public int MentionOnlyMentions { get; set; }
        public double SentimentCoverage { get; set; }
        public bool HasScoredSentiment { get; set; }
        public double CommunitySentiment { get; set; }
        public double Sentiment { get; set; }
        public double Impact { get; set; }
        public double PositiveImpact { get; set; }
        public double NegativeImpact { get; set; }
        public string Confidence { get; set; } = string.Empty;
    }

    private sealed class InfluentialCommentSummary
    {
        public string Player { get; set; } = string.Empty;
        public string CommentExcerpt { get; set; } = string.Empty;
        public int RedditScore { get; set; }
        public double Sentiment { get; set; }
        public double VoteWeight { get; set; }
        public double Contribution { get; set; }
        public string Thread { get; set; } = string.Empty;
    }

    private sealed class GateExclusionSummary
    {
        public int ActualScorableMentions { get; set; }
        public int Scored { get; set; }
        public int SemanticConfidenceOnly { get; set; }
        public int TargetConfidenceOnly { get; set; }
        public int FailedBoth { get; set; }
    }
}
