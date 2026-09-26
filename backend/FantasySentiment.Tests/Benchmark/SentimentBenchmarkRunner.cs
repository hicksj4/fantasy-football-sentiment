using System.Text;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using FantasySentiment.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Tests.Benchmark;

internal sealed class SentimentBenchmarkRunner
{
    private readonly BenchmarkCorpus _corpus;
    private readonly CommentTargetResolver _targetResolver;
    private readonly TargetContextBuilder _targetContextBuilder;
    private readonly ThreadContextBuilder _threadContextBuilder;
    private readonly VaderSentimentAnalyzer _sentimentAnalyzer;
    private readonly FantasyIntentClassifier _intentClassifier;

    public SentimentBenchmarkRunner(BenchmarkCorpus corpus)
    {
        _corpus = corpus;
        _targetResolver = new CommentTargetResolver(
            new PlayerMentionDetector(),
            NullLogger<CommentTargetResolver>.Instance);
        _targetContextBuilder = new TargetContextBuilder(new MentionContextExtractor());
        _threadContextBuilder = new ThreadContextBuilder();
        _sentimentAnalyzer = new VaderSentimentAnalyzer(
            new FantasySentimentAnalyzer(
                new BenchmarkOptionsMonitor<SentimentLexiconOptions>(corpus.SentimentLexicon)));
        _intentClassifier = new FantasyIntentClassifier(
            new BenchmarkOptionsMonitor<IntentLexiconOptions>(corpus.IntentLexicon));
    }

    public async Task<BenchmarkReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var failures = new List<BenchmarkFailure>();
        var report = new BenchmarkReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Analyzer = nameof(VaderSentimentAnalyzer),
            TargetResolver = nameof(CommentTargetResolver),
            FixtureCount = _corpus.Fixtures.Count
        };

        foreach (var fixture in _corpus.Fixtures)
        {
            var threadSummary = await EvaluateFixtureAsync(fixture, failures, cancellationToken);
            report.ByThread.Add(threadSummary);
            report.TotalComments += threadSummary.CommentCount;
            report.TotalExpectedTargets += threadSummary.ExpectedTargetCount;
            report.SentimentExcludedByContext += threadSummary.SentimentExcludedByContext;
            report.TargetMetrics.Add(threadSummary.TargetMetrics);
            report.SentimentMetrics.Add(threadSummary.SentimentMetrics);
            report.PerspectiveMetrics.Add(threadSummary.PerspectiveMetrics);
            report.IntentMetrics.Add(threadSummary.IntentMetrics);
            AddConfusionMatrix(report.SentimentConfusionMatrix, threadSummary.SentimentConfusionMatrix);
            AddCounts(report.ContextRequirementCounts, threadSummary.ContextRequirementCounts);
        }

        report.CommentsWithoutExpectedTargets = _corpus.Fixtures
            .SelectMany(fixture => fixture.Comments)
            .Count(comment => comment.Expected.Targets.Count == 0);
        EnsureContextKeys(report.ContextRequirementCounts);
        report.FailureCategories = failures
            .GroupBy(failure => failure.Category, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        report.PerspectiveFailureCategories = CategorizeFailures(failures, "Perspective", CategorizePerspectiveFailure);
        report.IntentFailureCategories = CategorizeFailures(failures, "Intent", CategorizeIntentFailure);
        report.SentimentFailureCategories = CategorizeFailures(failures, "Sentiment", CategorizeSentimentFailure);
        report.PerspectiveFailureExamples = failures
            .Where(failure => failure.Category.StartsWith("Perspective:", StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToList();
        report.IntentFailureExamples = failures
            .Where(failure => failure.Category.StartsWith("Intent:", StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToList();
        report.SentimentFailureExamples = failures
            .Where(failure => failure.Category.StartsWith("Sentiment:", StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToList();
        report.DebatableLabels = failures
            .Where(failure =>
                failure.Notes?.Contains("ambiguous", StringComparison.OrdinalIgnoreCase) == true
                || failure.Notes?.Contains("debatable", StringComparison.OrdinalIgnoreCase) == true
                || string.Equals(failure.ContextRequirement, BenchmarkContextRequirements.Ambiguous, StringComparison.OrdinalIgnoreCase)
                || string.Equals(failure.ContextRequirement, BenchmarkContextRequirements.GameEventContext, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();
        report.RepresentativeFailures = failures.Take(40).ToList();

        return report;
    }

    public string ToMarkdown(BenchmarkReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Sentiment Benchmark Baseline");
        builder.AppendLine();
        builder.AppendLine($"Generated: {report.GeneratedAtUtc:O}");
        builder.AppendLine($"Analyzer: {report.Analyzer}");
        builder.AppendLine($"Target resolver: {report.TargetResolver}");
        builder.AppendLine($"Fixtures: {report.FixtureCount}");
        builder.AppendLine($"Comments: {report.TotalComments}");
        builder.AppendLine($"Expected targets: {report.TotalExpectedTargets}");
        builder.AppendLine($"Comments with no expected targets: {report.CommentsWithoutExpectedTargets}");
        builder.AppendLine($"Scorable sentiment targets: {report.ScorableSentimentTargets}");
        builder.AppendLine($"Sentiment targets excluded by context: {report.SentimentExcludedByContext}");
        builder.AppendLine();
        builder.AppendLine("## Context Counts");
        builder.AppendLine();
        foreach (var (context, count) in report.ContextRequirementCounts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"- {context}: {count}");
        }

        builder.AppendLine();
        builder.AppendLine("## Metrics");
        builder.AppendLine();
        builder.AppendLine($"Target precision: {report.TargetMetrics.Precision:0.###}");
        builder.AppendLine($"Target recall: {report.TargetMetrics.Recall:0.###}");
        builder.AppendLine($"Target F1: {report.TargetMetrics.F1:0.###}");
        builder.AppendLine($"Sentiment directional accuracy: {report.SentimentMetrics.Accuracy:0.###} ({report.SentimentMetrics.Correct}/{report.SentimentMetrics.Total})");
        builder.AppendLine($"Perspective accuracy: {report.PerspectiveMetrics.Accuracy:0.###} ({report.PerspectiveMetrics.Correct}/{report.PerspectiveMetrics.Total})");
        builder.AppendLine($"Intent accuracy: {report.IntentMetrics.Accuracy:0.###} ({report.IntentMetrics.Correct}/{report.IntentMetrics.Total})");
        builder.AppendLine();
        builder.AppendLine("## By Thread");
        builder.AppendLine();
        foreach (var thread in report.ByThread)
        {
            builder.AppendLine($"- {thread.ThreadName}: comments={thread.CommentCount}, targetF1={thread.TargetMetrics.F1:0.###}, sentiment={thread.SentimentMetrics.Accuracy:0.###}, perspective={thread.PerspectiveMetrics.Accuracy:0.###}, intent={thread.IntentMetrics.Accuracy:0.###}");
        }

        builder.AppendLine();
        builder.AppendLine("## Sentiment Confusion Matrix");
        builder.AppendLine();
        builder.AppendLine("| Expected | Actual Negative | Actual Neutral | Actual Positive |");
        builder.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var expected in new[] { BenchmarkDirections.Negative, BenchmarkDirections.Neutral, BenchmarkDirections.Positive })
        {
            var row = report.SentimentConfusionMatrix[expected];
            builder.AppendLine($"| {expected} | {row[BenchmarkDirections.Negative]} | {row[BenchmarkDirections.Neutral]} | {row[BenchmarkDirections.Positive]} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Failure Categories");
        builder.AppendLine();
        foreach (var (category, count) in report.FailureCategories)
        {
            builder.AppendLine($"- {category}: {count}");
        }

        AppendAuditSection(builder, "Perspective Failure Audit", report.PerspectiveFailureCategories, report.PerspectiveFailureExamples);
        AppendAuditSection(builder, "Intent Failure Audit", report.IntentFailureCategories, report.IntentFailureExamples);
        AppendAuditSection(builder, "Sentiment Failure Audit", report.SentimentFailureCategories, report.SentimentFailureExamples);

        builder.AppendLine();
        builder.AppendLine("## Representative Failures");
        builder.AppendLine();
        foreach (var failure in report.RepresentativeFailures)
        {
            builder.AppendLine($"- [{failure.Category}] {failure.ThreadName} / {failure.CommentId}");
            builder.AppendLine($"  - Text: {Trim(failure.Body, 180)}");
            builder.AppendLine($"  - Expected: {failure.Expected}");
            builder.AppendLine($"  - Actual: {failure.Actual}");
            if (!string.IsNullOrWhiteSpace(failure.Notes))
            {
                builder.AppendLine($"  - Notes: {failure.Notes}");
            }
        }

        return builder.ToString();
    }

    private async Task<ThreadBenchmarkSummary> EvaluateFixtureAsync(
        BenchmarkFixture fixture,
        List<BenchmarkFailure> failures,
        CancellationToken cancellationToken)
    {
        var commentsById = fixture.Comments.ToDictionary(
            comment => comment.Id,
            StringComparer.OrdinalIgnoreCase);
        var resolutionByCommentId = new Dictionary<string, CommentTargetResolution>(StringComparer.OrdinalIgnoreCase);
        var processingCommentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var actualByCommentId = new Dictionary<string, List<ActualTargetResult>>(StringComparer.OrdinalIgnoreCase);
        var summary = new ThreadBenchmarkSummary
        {
            ThreadName = fixture.Thread.Name,
            CommentCount = fixture.Comments.Count,
            ExpectedTargetCount = fixture.Comments.Sum(comment => comment.Expected.Targets.Count)
        };
        summary.ContextRequirementCounts = fixture.Comments
            .GroupBy(comment => comment.Expected.ContextRequirement, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var comment in fixture.Comments)
        {
            var resolution = ResolveCommentTargets(
                fixture,
                comment,
                commentsById,
                resolutionByCommentId,
                processingCommentIds);
            var actualTargets = await AnalyzeTargetsAsync(
                fixture,
                comment,
                commentsById,
                resolution,
                cancellationToken);
            actualByCommentId[comment.Id] = actualTargets;

            ScoreTargetResolution(fixture, comment, resolution, actualTargets, summary, failures);
            ScoreMatchedTargets(fixture, comment, actualTargets, summary, failures);
        }

        return summary;
    }

    private CommentTargetResolution ResolveCommentTargets(
        BenchmarkFixture fixture,
        BenchmarkComment comment,
        IReadOnlyDictionary<string, BenchmarkComment> commentsById,
        Dictionary<string, CommentTargetResolution> resolutionByCommentId,
        HashSet<string> processingCommentIds)
    {
        if (resolutionByCommentId.TryGetValue(comment.Id, out var cached))
        {
            return cached;
        }

        if (!processingCommentIds.Add(comment.Id))
        {
            var cycle = CommentTargetResolution.Unresolved("cycle detected");
            resolutionByCommentId[comment.Id] = cycle;
            return cycle;
        }

        IReadOnlyList<ResolvedCommentTarget> parentTargets = [];
        if (!string.IsNullOrWhiteSpace(comment.ParentId)
            && commentsById.TryGetValue(comment.ParentId, out var parentComment))
        {
            parentTargets = ResolveCommentTargets(
                fixture,
                parentComment,
                commentsById,
                resolutionByCommentId,
                processingCommentIds).Targets;
        }

        var resolution = _targetResolver.Resolve(new CommentTargetResolutionRequest(
            comment.Id,
            comment.Body,
            fixture.Thread.Name,
            _corpus.Players,
            parentTargets));
        resolutionByCommentId[comment.Id] = resolution;
        processingCommentIds.Remove(comment.Id);
        return resolution;
    }

    private async Task<List<ActualTargetResult>> AnalyzeTargetsAsync(
        BenchmarkFixture fixture,
        BenchmarkComment comment,
        IReadOnlyDictionary<string, BenchmarkComment> commentsById,
        CommentTargetResolution resolution,
        CancellationToken cancellationToken)
    {
        var actual = new List<ActualTargetResult>();
        var parentBody = !string.IsNullOrWhiteSpace(comment.ParentId)
                         && commentsById.TryGetValue(comment.ParentId, out var parent)
            ? parent.Body
            : null;

        foreach (var target in resolution.Targets)
        {
            var resolvedContext = _targetContextBuilder.Build(
                fixture.Thread.Name,
                parentBody,
                comment.Body,
                target,
                resolution.Targets);
            var match = resolvedContext.Target.ToMatch();
            var analysisContext = _threadContextBuilder.Build(resolvedContext);
            var sentiment = await _sentimentAnalyzer.AnalyzeAsync(
                new TargetedSentimentRequest(
                    comment.Body,
                    analysisContext,
                    match,
                    resolvedContext.TargetText,
                    resolvedContext.Perspective),
                cancellationToken);

            actual.Add(new ActualTargetResult(
                target.PlayerName,
                BenchmarkDirections.FromScore(sentiment.FinalSentimentScore),
                sentiment.FinalSentimentScore,
                resolvedContext.Perspective,
                _intentClassifier.Classify(comment.Body, match),
                target.AttributionMethod,
                target.Confidence,
                resolvedContext.TargetText,
                sentiment.Diagnostics));
        }

        return actual;
    }

    private static void ScoreTargetResolution(
        BenchmarkFixture fixture,
        BenchmarkComment comment,
        CommentTargetResolution resolution,
        IReadOnlyList<ActualTargetResult> actualTargets,
        ThreadBenchmarkSummary summary,
        List<BenchmarkFailure> failures)
    {
        var expectedNames = comment.Expected.Targets
            .Select(target => target.Player)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualNames = actualTargets
            .Select(target => target.Player)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in expectedNames)
        {
            if (actualNames.Contains(name))
            {
                summary.TargetMetrics.TruePositive++;
                continue;
            }

            summary.TargetMetrics.FalseNegative++;
            failures.Add(new BenchmarkFailure
            {
                Category = "TargetFalseNegative",
                ThreadName = fixture.Thread.Name,
                CommentId = comment.Id,
                Body = comment.Body,
                Expected = name,
                Actual = resolution.UnresolvedReason is null
                    ? "not detected"
                    : $"not detected ({resolution.UnresolvedReason})",
                ContextRequirement = comment.Expected.ContextRequirement,
                Notes = comment.Notes
            });
        }

        foreach (var name in actualNames)
        {
            if (expectedNames.Contains(name))
            {
                continue;
            }

            summary.TargetMetrics.FalsePositive++;
            failures.Add(new BenchmarkFailure
            {
                Category = "TargetFalsePositive",
                ThreadName = fixture.Thread.Name,
                CommentId = comment.Id,
                Body = comment.Body,
                Expected = expectedNames.Count == 0 ? "no target" : string.Join(", ", expectedNames),
                Actual = name,
                ContextRequirement = comment.Expected.ContextRequirement,
                Notes = comment.Notes
            });
        }
    }

    private static void ScoreMatchedTargets(
        BenchmarkFixture fixture,
        BenchmarkComment comment,
        IReadOnlyList<ActualTargetResult> actualTargets,
        ThreadBenchmarkSummary summary,
        List<BenchmarkFailure> failures)
    {
        foreach (var expectedTarget in comment.Expected.Targets)
        {
            var actual = actualTargets.FirstOrDefault(target =>
                string.Equals(target.Player, expectedTarget.Player, StringComparison.OrdinalIgnoreCase));
            if (actual is null)
            {
                continue;
            }

            var expectedDirection = BenchmarkDirections.FromExpectedLabel(expectedTarget.Sentiment);
            if (ShouldEvaluateOrdinarySentiment(comment, expectedDirection))
            {
                var sentimentCorrect = string.Equals(
                    expectedDirection,
                    actual.SentimentDirection,
                    StringComparison.OrdinalIgnoreCase);
                summary.SentimentMetrics.Add(sentimentCorrect);
                summary.SentimentConfusionMatrix[expectedDirection!][actual.SentimentDirection]++;
                if (!sentimentCorrect)
                {
                    failures.Add(new BenchmarkFailure
                    {
                        Category = FailureCategory("Sentiment", expectedTarget),
                        ThreadName = fixture.Thread.Name,
                        CommentId = comment.Id,
                        Body = comment.Body,
                        Expected = $"{expectedTarget.Player} {expectedDirection}",
                        Actual = $"{actual.Player} {actual.SentimentDirection} ({actual.SentimentScore:0.###})",
                        ContextRequirement = comment.Expected.ContextRequirement,
                        Notes = expectedTarget.Notes ?? comment.Notes,
                        Diagnostics = actual.Diagnostics
                    });
                }
            }
            else if (expectedDirection is not null)
            {
                summary.SentimentExcludedByContext++;
            }
            else if (IsContextExcludedFromOrdinarySentiment(comment))
            {
                summary.SentimentExcludedByContext++;
            }

            if (!string.Equals(expectedTarget.Perspective, TargetPerspectives.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                var perspectiveCorrect = string.Equals(
                    expectedTarget.Perspective,
                    actual.Perspective,
                    StringComparison.OrdinalIgnoreCase);
                summary.PerspectiveMetrics.Add(perspectiveCorrect);
                if (!perspectiveCorrect)
                {
                    failures.Add(new BenchmarkFailure
                    {
                        Category = $"Perspective:{expectedTarget.Perspective}",
                        ThreadName = fixture.Thread.Name,
                        CommentId = comment.Id,
                        Body = comment.Body,
                        Expected = $"{expectedTarget.Player} {expectedTarget.Perspective}",
                        Actual = $"{actual.Player} {actual.Perspective}",
                        ContextRequirement = comment.Expected.ContextRequirement,
                        Notes = expectedTarget.Notes ?? comment.Notes,
                        Diagnostics = actual.Diagnostics
                    });
                }
            }

            if (expectedTarget.Intents.Count > 0)
            {
                var intentCorrect = expectedTarget.Intents.Contains(actual.Intent, StringComparer.OrdinalIgnoreCase);
                summary.IntentMetrics.Add(intentCorrect);
                if (!intentCorrect)
                {
                    failures.Add(new BenchmarkFailure
                    {
                        Category = FailureCategory("Intent", expectedTarget),
                        ThreadName = fixture.Thread.Name,
                        CommentId = comment.Id,
                        Body = comment.Body,
                        Expected = $"{expectedTarget.Player} {string.Join("/", expectedTarget.Intents)}",
                        Actual = $"{actual.Player} {actual.Intent}",
                        ContextRequirement = comment.Expected.ContextRequirement,
                        Notes = expectedTarget.Notes ?? comment.Notes,
                        Diagnostics = actual.Diagnostics
                    });
                }
            }
        }
    }

    private static bool ShouldEvaluateOrdinarySentiment(
        BenchmarkComment comment,
        string? expectedDirection)
    {
        if (expectedDirection is null)
        {
            return false;
        }

        return !IsContextExcludedFromOrdinarySentiment(comment);
    }

    private static bool IsContextExcludedFromOrdinarySentiment(BenchmarkComment comment)
    {
        return string.Equals(comment.Expected.ContextRequirement, BenchmarkContextRequirements.GameEventContext, StringComparison.OrdinalIgnoreCase)
               || string.Equals(comment.Expected.ContextRequirement, BenchmarkContextRequirements.Ambiguous, StringComparison.OrdinalIgnoreCase);
    }

    private static string FailureCategory(string prefix, BenchmarkExpectedTarget target)
    {
        var reason = target.Reasons.FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
        return reason is null ? prefix : $"{prefix}:{reason}";
    }

    private static void AddConfusionMatrix(
        Dictionary<string, Dictionary<string, int>> aggregate,
        Dictionary<string, Dictionary<string, int>> addition)
    {
        foreach (var expected in addition.Keys)
        {
            foreach (var actual in addition[expected].Keys)
            {
                aggregate[expected][actual] += addition[expected][actual];
            }
        }
    }

    private static void AddCounts(Dictionary<string, int> aggregate, IReadOnlyDictionary<string, int> addition)
    {
        foreach (var (key, count) in addition)
        {
            aggregate[key] = aggregate.TryGetValue(key, out var existing)
                ? existing + count
                : count;
        }
    }

    private static void EnsureContextKeys(Dictionary<string, int> counts)
    {
        foreach (var key in new[]
                 {
                     BenchmarkContextRequirements.SelfContained,
                     BenchmarkContextRequirements.ReplyContext,
                     BenchmarkContextRequirements.GameEventContext,
                     BenchmarkContextRequirements.Ambiguous
                 })
        {
            counts.TryAdd(key, 0);
        }
    }

    private static Dictionary<string, int> CategorizeFailures(
        IReadOnlyList<BenchmarkFailure> failures,
        string prefix,
        Func<BenchmarkFailure, string> classifier)
    {
        return failures
            .Where(failure => failure.Category.StartsWith($"{prefix}:", StringComparison.OrdinalIgnoreCase))
            .GroupBy(classifier, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    }

    private static string CategorizePerspectiveFailure(BenchmarkFailure failure)
    {
        var body = failure.Body.ToLowerInvariant();
        if (failure.Expected.Contains(TargetPerspectives.OpponentRoster, StringComparison.OrdinalIgnoreCase))
        {
            return body.Contains("opp ", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("opponent", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("facing", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("against", StringComparison.OrdinalIgnoreCase)
                ? "Explicit opponent language missed"
                : "Implicit opponent language missed";
        }

        if (failure.Expected.Contains(TargetPerspectives.OwnRoster, StringComparison.OrdinalIgnoreCase))
        {
            return body.Contains("start", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("bench", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("lineup", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("draft", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("my ", StringComparison.OrdinalIgnoreCase)
                ? "Lineup regret or ownership language missed"
                : "Implicit ownership missed";
        }

        return failure.Actual.Contains(TargetPerspectives.Unknown, StringComparison.OrdinalIgnoreCase)
            ? "Incorrectly classified Unknown"
            : "Other perspective mismatch";
    }

    private static string CategorizeIntentFailure(BenchmarkFailure failure)
    {
        var expected = failure.Expected.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "Unknown";
        if (expected.Contains('/'))
        {
            return "Multiple plausible intents vs single-intent classifier";
        }

        return expected;
    }

    private static string CategorizeSentimentFailure(BenchmarkFailure failure)
    {
        if (failure.Category.Contains("OpponentPerspective", StringComparison.OrdinalIgnoreCase))
        {
            return "OpponentThreat";
        }

        if (failure.Category.Contains("Sarcasm", StringComparison.OrdinalIgnoreCase))
        {
            return "Sarcasm";
        }

        if (failure.Category.Contains("Comparison", StringComparison.OrdinalIgnoreCase))
        {
            return "Comparison";
        }

        if (failure.Category.Contains("DraftRegret", StringComparison.OrdinalIgnoreCase))
        {
            return "DraftRegret";
        }

        if (failure.Category.Contains("LineupRegret", StringComparison.OrdinalIgnoreCase))
        {
            return "LineupRegret";
        }

        if (failure.Category.Contains("Injury", StringComparison.OrdinalIgnoreCase))
        {
            return "Injury";
        }

        if (failure.Category.Contains("Usage", StringComparison.OrdinalIgnoreCase))
        {
            return "Usage";
        }

        if (failure.Body.Contains('?', StringComparison.Ordinal))
        {
            return "Question";
        }

        if (string.Equals(failure.ContextRequirement, BenchmarkContextRequirements.GameEventContext, StringComparison.OrdinalIgnoreCase))
        {
            return "GameEventRequired";
        }

        if (failure.Body.Contains("cooked", StringComparison.OrdinalIgnoreCase)
            || failure.Body.Contains("him", StringComparison.OrdinalIgnoreCase)
            || failure.Body.Contains("goat", StringComparison.OrdinalIgnoreCase)
            || failure.Body.Contains("ass", StringComparison.OrdinalIgnoreCase))
        {
            return "Slang";
        }

        if (failure.Category.Contains("QuarterbackPlay", StringComparison.OrdinalIgnoreCase)
            || failure.Category.Contains("Coaching", StringComparison.OrdinalIgnoreCase))
        {
            return "PlayerVsSituation";
        }

        return "Other";
    }

    private static void AppendAuditSection(
        StringBuilder builder,
        string title,
        IReadOnlyDictionary<string, int> categories,
        IReadOnlyList<BenchmarkFailure> examples)
    {
        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        foreach (var (category, count) in categories)
        {
            builder.AppendLine($"- {category}: {count}");
        }

        if (examples.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("Examples:");
        foreach (var example in examples)
        {
            builder.AppendLine($"- [{example.Category}] {example.CommentId}: {Trim(example.Body, 150)}");
            builder.AppendLine($"  - Expected: {example.Expected}");
            builder.AppendLine($"  - Actual: {example.Actual}");
        }
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
}
