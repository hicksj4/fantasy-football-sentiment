using System.Text;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using FantasySentiment.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Tests.Benchmark;

internal sealed class SentimentBenchmarkRunner
{
    private const int PreviousCommentCount = 243;
    private const int PreviousExpectedTargetCount = 213;
    private const double PreviousTargetF1 = 0.957;
    private const double PreviousFantasyOutlookAccuracy = 0.505;
    private const double PreviousPerspectiveAccuracy = 0.125;
    private const double PreviousSingleIntentAccuracy = 0.308;

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
        var labelAudits = new List<BenchmarkFailure>();
        var divergences = new List<SemanticDivergenceExample>();
        var coOccurrences = new List<ActionSignalCoOccurrenceExample>();
        var report = new BenchmarkReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Analyzer = nameof(VaderSentimentAnalyzer),
            TargetResolver = nameof(CommentTargetResolver),
            FixtureCount = _corpus.Fixtures.Count
        };

        foreach (var fixture in _corpus.Fixtures)
        {
            var threadSummary = await EvaluateFixtureAsync(
                fixture,
                failures,
                labelAudits,
                divergences,
                coOccurrences,
                cancellationToken);
            report.ByThread.Add(threadSummary);
            report.TotalComments += threadSummary.CommentCount;
            report.TotalExpectedTargets += threadSummary.ExpectedTargetCount;
            report.FantasyOutlookExcludedByContext += threadSummary.FantasyOutlookExcludedByContext;
            report.FantasyOutlookLabeledTargets += threadSummary.FantasyOutlookLabeledTargets;
            report.PlayerEvaluationLabeledTargets += threadSummary.PlayerEvaluationLabeledTargets;
            report.PerspectiveLabeledTargets += threadSummary.PerspectiveLabeledTargets;
            report.FantasyActionLabeledTargets += threadSummary.FantasyActionLabeledTargets;
            report.FantasyActionLabelCount += threadSummary.FantasyActionLabelCount;
            report.DiscussionSignalLabeledTargets += threadSummary.DiscussionSignalLabeledTargets;
            report.DiscussionSignalLabelCount += threadSummary.DiscussionSignalLabelCount;
            report.TargetMetrics.Add(threadSummary.TargetMetrics);
            report.FantasyOutlookMetrics.Add(threadSummary.FantasyOutlookMetrics);
            report.PlayerEvaluationMetrics.Add(threadSummary.PlayerEvaluationMetrics);
            report.PerspectiveMetrics.Add(threadSummary.PerspectiveMetrics);
            report.FantasyActionMetrics.Add(threadSummary.FantasyActionMetrics);
            report.DiscussionSignalMetrics.Add(threadSummary.DiscussionSignalMetrics);
            AddConfusionMatrix(report.FantasyOutlookConfusionMatrix, threadSummary.FantasyOutlookConfusionMatrix);
            AddConfusionMatrix(report.PlayerEvaluationConfusionMatrix, threadSummary.PlayerEvaluationConfusionMatrix);
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
        report.FantasyActionFailureCategories = CategorizeFailures(failures, "FantasyAction", CategorizeSetFailure);
        report.DiscussionSignalFailureCategories = CategorizeFailures(failures, "DiscussionSignal", CategorizeSetFailure);
        report.FantasyOutlookFailureCategories = CategorizeFailures(failures, "FantasyOutlook", CategorizeSemanticFailure);
        report.PlayerEvaluationFailureCategories = CategorizeFailures(failures, "PlayerEvaluation", CategorizeSemanticFailure);
        report.PerspectiveFailureExamples = FailuresFor(failures, "Perspective").Take(12).ToList();
        report.FantasyActionFailureExamples = FailuresFor(failures, "FantasyAction").Take(12).ToList();
        report.DiscussionSignalFailureExamples = FailuresFor(failures, "DiscussionSignal").Take(12).ToList();
        report.FantasyOutlookFailureExamples = FailuresFor(failures, "FantasyOutlook").Take(12).ToList();
        report.PlayerEvaluationFailureExamples = FailuresFor(failures, "PlayerEvaluation").Take(12).ToList();
        report.PlayerEvaluationOutlookComparableCount = _corpus.Fixtures
            .SelectMany(fixture => fixture.Comments)
            .SelectMany(comment => comment.Expected.Targets)
            .Count(target =>
                BenchmarkDirections.FromExpectedLabel(target.FantasyOutlook) is not null
                && BenchmarkDirections.FromExpectedLabel(target.PlayerEvaluation) is not null);
        report.PlayerEvaluationOutlookDivergenceCount = divergences.Count;
        report.ActionSignalCoOccurrenceCount = coOccurrences.Count;
        report.PlayerEvaluationOutlookDivergences = divergences.Take(20).ToList();
        report.ActionSignalCoOccurrences = coOccurrences.Take(20).ToList();
        report.DebatableLabels = labelAudits.Take(30).ToList();
        report.RepresentativeFailures = failures.Take(40).ToList();

        return report;
    }

    public string ToMarkdown(BenchmarkReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Semantic Benchmark Baseline");
        builder.AppendLine();
        builder.AppendLine($"Generated: {report.GeneratedAtUtc:O}");
        builder.AppendLine($"Analyzer: {report.Analyzer}");
        builder.AppendLine($"Target resolver: {report.TargetResolver}");
        builder.AppendLine($"Fixtures: {report.FixtureCount}");
        builder.AppendLine($"Comments: {report.TotalComments}");
        builder.AppendLine($"Expected targets: {report.TotalExpectedTargets}");
        builder.AppendLine($"Comments with no expected targets: {report.CommentsWithoutExpectedTargets}");
        builder.AppendLine($"Scorable fantasy-outlook targets: {report.ScorableFantasyOutlookTargets}");
        builder.AppendLine($"Fantasy-outlook targets excluded by context: {report.FantasyOutlookExcludedByContext}");
        builder.AppendLine();
        builder.AppendLine("## Semantic Coverage");
        builder.AppendLine();
        builder.AppendLine($"- FantasyOutlook labeled targets: {report.FantasyOutlookLabeledTargets}");
        builder.AppendLine($"- PlayerEvaluation labeled targets: {report.PlayerEvaluationLabeledTargets}");
        builder.AppendLine($"- Perspective labeled targets: {report.PerspectiveLabeledTargets}");
        builder.AppendLine($"- FantasyAction labeled targets: {report.FantasyActionLabeledTargets} ({report.FantasyActionLabelCount} labels)");
        builder.AppendLine($"- DiscussionSignal labeled targets: {report.DiscussionSignalLabeledTargets} ({report.DiscussionSignalLabelCount} labels)");
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
        builder.AppendLine($"Fantasy Outlook directional accuracy: {report.FantasyOutlookMetrics.Accuracy:0.###} ({report.FantasyOutlookMetrics.Correct}/{report.FantasyOutlookMetrics.Total})");
        builder.AppendLine($"Player Evaluation directional accuracy: {report.PlayerEvaluationMetrics.Accuracy:0.###} ({report.PlayerEvaluationMetrics.Correct}/{report.PlayerEvaluationMetrics.Total})");
        builder.AppendLine($"Perspective accuracy: {report.PerspectiveMetrics.Accuracy:0.###} ({report.PerspectiveMetrics.Correct}/{report.PerspectiveMetrics.Total})");
        builder.AppendLine($"Fantasy Action precision/recall/F1: {report.FantasyActionMetrics.Precision:0.###}/{report.FantasyActionMetrics.Recall:0.###}/{report.FantasyActionMetrics.F1:0.###} ({report.FantasyActionMetrics.TruePositive} TP, {report.FantasyActionMetrics.FalsePositive} FP, {report.FantasyActionMetrics.FalseNegative} FN)");
        builder.AppendLine($"Discussion Signal precision/recall/F1: {report.DiscussionSignalMetrics.Precision:0.###}/{report.DiscussionSignalMetrics.Recall:0.###}/{report.DiscussionSignalMetrics.F1:0.###} ({report.DiscussionSignalMetrics.TruePositive} TP, {report.DiscussionSignalMetrics.FalsePositive} FP, {report.DiscussionSignalMetrics.FalseNegative} FN)");
        builder.AppendLine();
        builder.AppendLine("The production classifier still emits one legacy intent label. The benchmark projects that label into either one fantasy action, one discussion signal, or neither, so multi-label recall loss is expected and visible.");
        builder.AppendLine();
        builder.AppendLine("## Old Contract Comparison");
        builder.AppendLine();
        builder.AppendLine($"Previous benchmark interpretation: comments={PreviousCommentCount}, expectedTargets={PreviousExpectedTargetCount}, targetF1={PreviousTargetF1:0.###}, sentimentAccuracy={PreviousFantasyOutlookAccuracy:0.###}, perspectiveAccuracy={PreviousPerspectiveAccuracy:0.###}, singleIntentAccuracy={PreviousSingleIntentAccuracy:0.###}.");
        builder.AppendLine("Metric changes in this report come from semantic relabeling and reporting changes only; production sentiment, target resolution, perspective, and intent logic were not tuned.");
        builder.AppendLine();
        builder.AppendLine("## By Thread");
        builder.AppendLine();
        foreach (var thread in report.ByThread)
        {
            builder.AppendLine($"- {thread.ThreadName}: comments={thread.CommentCount}, targetF1={thread.TargetMetrics.F1:0.###}, fantasyOutlook={thread.FantasyOutlookMetrics.Accuracy:0.###}, playerEvaluation={thread.PlayerEvaluationMetrics.Accuracy:0.###}, perspective={thread.PerspectiveMetrics.Accuracy:0.###}, actions={thread.FantasyActionMetrics.F1:0.###}, signals={thread.DiscussionSignalMetrics.F1:0.###}");
        }

        AppendConfusionMatrix(builder, "Fantasy Outlook Confusion Matrix", report.FantasyOutlookConfusionMatrix);
        AppendConfusionMatrix(builder, "Player Evaluation Confusion Matrix", report.PlayerEvaluationConfusionMatrix);
        AppendDivergenceSection(builder, report);
        AppendCoOccurrenceSection(builder, report);

        builder.AppendLine();
        builder.AppendLine("## Failure Categories");
        builder.AppendLine();
        foreach (var (category, count) in report.FailureCategories)
        {
            builder.AppendLine($"- {category}: {count}");
        }

        AppendAuditSection(builder, "Fantasy Outlook Failure Audit", report.FantasyOutlookFailureCategories, report.FantasyOutlookFailureExamples);
        AppendAuditSection(builder, "Player Evaluation Failure Audit", report.PlayerEvaluationFailureCategories, report.PlayerEvaluationFailureExamples);
        AppendAuditSection(builder, "Perspective Failure Audit", report.PerspectiveFailureCategories, report.PerspectiveFailureExamples);
        AppendAuditSection(builder, "Fantasy Action Failure Audit", report.FantasyActionFailureCategories, report.FantasyActionFailureExamples);
        AppendAuditSection(builder, "Discussion Signal Failure Audit", report.DiscussionSignalFailureCategories, report.DiscussionSignalFailureExamples);
        AppendDebatableSection(builder, report.DebatableLabels);
        AppendRepresentativeFailures(builder, report.RepresentativeFailures);

        return builder.ToString();
    }

    private async Task<ThreadBenchmarkSummary> EvaluateFixtureAsync(
        BenchmarkFixture fixture,
        List<BenchmarkFailure> failures,
        List<BenchmarkFailure> labelAudits,
        List<SemanticDivergenceExample> divergences,
        List<ActionSignalCoOccurrenceExample> coOccurrences,
        CancellationToken cancellationToken)
    {
        var commentsById = fixture.Comments.ToDictionary(
            comment => comment.Id,
            StringComparer.OrdinalIgnoreCase);
        var resolutionByCommentId = new Dictionary<string, CommentTargetResolution>(StringComparer.OrdinalIgnoreCase);
        var processingCommentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

            ScoreTargetResolution(fixture, comment, resolution, actualTargets, summary, failures);
            ScoreMatchedTargets(
                fixture,
                comment,
                actualTargets,
                summary,
                failures,
                labelAudits,
                divergences,
                coOccurrences);
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
                    resolvedContext.Perspective,
                    target.AttributionMethod),
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
        List<BenchmarkFailure> failures,
        List<BenchmarkFailure> labelAudits,
        List<SemanticDivergenceExample> divergences,
        List<ActionSignalCoOccurrenceExample> coOccurrences)
    {
        foreach (var expectedTarget in comment.Expected.Targets)
        {
            ScoreSemanticCoverage(fixture, comment, expectedTarget, summary, labelAudits, divergences, coOccurrences);

            var actual = actualTargets.FirstOrDefault(target =>
                string.Equals(target.Player, expectedTarget.Player, StringComparison.OrdinalIgnoreCase));
            if (actual is null)
            {
                continue;
            }

            ScoreFantasyOutlook(fixture, comment, expectedTarget, actual, summary, failures);
            ScorePlayerEvaluation(fixture, comment, expectedTarget, actual, summary, failures);
            ScorePerspective(fixture, comment, expectedTarget, actual, summary, failures);
            ScoreProjectedSet(
                "FantasyAction",
                expectedTarget,
                NormalizeFantasyActions(expectedTarget),
                ProjectAction(actual.Intent),
                fixture,
                comment,
                actual,
                summary.FantasyActionMetrics,
                failures);
            ScoreProjectedSet(
                "DiscussionSignal",
                expectedTarget,
                NormalizeDiscussionSignals(expectedTarget),
                ProjectSignal(actual.Intent),
                fixture,
                comment,
                actual,
                summary.DiscussionSignalMetrics,
                failures);
        }
    }

    private static void ScoreFantasyOutlook(
        BenchmarkFixture fixture,
        BenchmarkComment comment,
        BenchmarkExpectedTarget expectedTarget,
        ActualTargetResult actual,
        ThreadBenchmarkSummary summary,
        List<BenchmarkFailure> failures)
    {
        var expectedOutlookDirection = BenchmarkDirections.FromExpectedLabel(expectedTarget.FantasyOutlook)
                                       ?? BenchmarkDirections.FromExpectedLabel(expectedTarget.Sentiment);
        if (expectedOutlookDirection is null || IsContextExcludedFromFantasyOutlook(comment))
        {
            return;
        }

        var outlookCorrect = string.Equals(
            expectedOutlookDirection,
            actual.SentimentDirection,
            StringComparison.OrdinalIgnoreCase);
        summary.FantasyOutlookMetrics.Add(outlookCorrect);
        summary.FantasyOutlookConfusionMatrix[expectedOutlookDirection][actual.SentimentDirection]++;
        if (!outlookCorrect)
        {
            failures.Add(new BenchmarkFailure
            {
                Category = FailureCategory("FantasyOutlook", expectedTarget),
                ThreadName = fixture.Thread.Name,
                CommentId = comment.Id,
                Body = comment.Body,
                Expected = $"{expectedTarget.Player} {expectedOutlookDirection}",
                Actual = $"{actual.Player} {actual.SentimentDirection} ({actual.SentimentScore:0.###})",
                ContextRequirement = comment.Expected.ContextRequirement,
                Notes = expectedTarget.Notes ?? comment.Notes,
                Diagnostics = actual.Diagnostics
            });
        }
    }

    private static void ScorePlayerEvaluation(
        BenchmarkFixture fixture,
        BenchmarkComment comment,
        BenchmarkExpectedTarget expectedTarget,
        ActualTargetResult actual,
        ThreadBenchmarkSummary summary,
        List<BenchmarkFailure> failures)
    {
        var expectedEvaluationDirection = BenchmarkDirections.FromExpectedLabel(expectedTarget.PlayerEvaluation);
        if (expectedEvaluationDirection is null)
        {
            return;
        }

        var evaluationCorrect = string.Equals(
            expectedEvaluationDirection,
            actual.SentimentDirection,
            StringComparison.OrdinalIgnoreCase);
        summary.PlayerEvaluationMetrics.Add(evaluationCorrect);
        summary.PlayerEvaluationConfusionMatrix[expectedEvaluationDirection][actual.SentimentDirection]++;
        if (!evaluationCorrect)
        {
            failures.Add(new BenchmarkFailure
            {
                Category = FailureCategory("PlayerEvaluation", expectedTarget),
                ThreadName = fixture.Thread.Name,
                CommentId = comment.Id,
                Body = comment.Body,
                Expected = $"{expectedTarget.Player} {expectedEvaluationDirection}",
                Actual = $"{actual.Player} {actual.SentimentDirection} ({actual.SentimentScore:0.###})",
                ContextRequirement = comment.Expected.ContextRequirement,
                Notes = expectedTarget.Notes ?? comment.Notes,
                Diagnostics = actual.Diagnostics
            });
        }
    }

    private static void ScorePerspective(
        BenchmarkFixture fixture,
        BenchmarkComment comment,
        BenchmarkExpectedTarget expectedTarget,
        ActualTargetResult actual,
        ThreadBenchmarkSummary summary,
        List<BenchmarkFailure> failures)
    {
        if (string.Equals(expectedTarget.Perspective, TargetPerspectives.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

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

    private static void ScoreSemanticCoverage(
        BenchmarkFixture fixture,
        BenchmarkComment comment,
        BenchmarkExpectedTarget target,
        ThreadBenchmarkSummary summary,
        List<BenchmarkFailure> labelAudits,
        List<SemanticDivergenceExample> divergences,
        List<ActionSignalCoOccurrenceExample> coOccurrences)
    {
        var fantasyOutlookDirection = BenchmarkDirections.FromExpectedLabel(target.FantasyOutlook)
                                      ?? BenchmarkDirections.FromExpectedLabel(target.Sentiment);
        var playerEvaluationDirection = BenchmarkDirections.FromExpectedLabel(target.PlayerEvaluation);
        var actions = NormalizeFantasyActions(target);
        var signals = NormalizeDiscussionSignals(target);

        if (fantasyOutlookDirection is not null)
        {
            summary.FantasyOutlookLabeledTargets++;
        }

        if (playerEvaluationDirection is not null)
        {
            summary.PlayerEvaluationLabeledTargets++;
        }

        if (!string.Equals(target.Perspective, TargetPerspectives.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            summary.PerspectiveLabeledTargets++;
        }

        if (actions.Count > 0)
        {
            summary.FantasyActionLabeledTargets++;
            summary.FantasyActionLabelCount += actions.Count;
        }

        if (signals.Count > 0)
        {
            summary.DiscussionSignalLabeledTargets++;
            summary.DiscussionSignalLabelCount += signals.Count;
        }

        if (IsContextExcludedFromFantasyOutlook(comment))
        {
            summary.FantasyOutlookExcludedByContext++;
        }

        if (fantasyOutlookDirection is not null
            && playerEvaluationDirection is not null
            && !string.Equals(fantasyOutlookDirection, playerEvaluationDirection, StringComparison.OrdinalIgnoreCase))
        {
            divergences.Add(new SemanticDivergenceExample
            {
                ThreadName = fixture.Thread.Name,
                CommentId = comment.Id,
                Body = comment.Body,
                Player = target.Player,
                FantasyOutlook = target.FantasyOutlook,
                PlayerEvaluation = target.PlayerEvaluation,
                Reasons = string.Join("/", target.Reasons),
                Notes = target.Notes ?? comment.Notes
            });
        }

        if (actions.Count > 0 && signals.Count > 0)
        {
            coOccurrences.Add(new ActionSignalCoOccurrenceExample
            {
                ThreadName = fixture.Thread.Name,
                CommentId = comment.Id,
                Body = comment.Body,
                Player = target.Player,
                FantasyActions = string.Join("/", actions),
                DiscussionSignals = string.Join("/", signals),
                Notes = target.Notes ?? comment.Notes
            });
        }

        if (target.IsDebatable || target.AlternativeLabels.Count > 0)
        {
            labelAudits.Add(new BenchmarkFailure
            {
                Category = "DebatableAnnotation",
                ThreadName = fixture.Thread.Name,
                CommentId = comment.Id,
                Body = comment.Body,
                Expected = $"{target.Player}: outlook={target.FantasyOutlook}, playerEvaluation={target.PlayerEvaluation}, actions={JoinOrNone(actions)}, signals={JoinOrNone(signals)}, perspective={target.Perspective}",
                Actual = target.AlternativeLabels.Count == 0
                    ? "No explicit alternative supplied"
                    : string.Join("; ", target.AlternativeLabels),
                ContextRequirement = comment.Expected.ContextRequirement,
                Notes = target.Notes ?? comment.Notes
            });
        }
    }

    private static void ScoreProjectedSet(
        string prefix,
        BenchmarkExpectedTarget expectedTarget,
        IReadOnlySet<string> expected,
        IReadOnlySet<string> actual,
        BenchmarkFixture fixture,
        BenchmarkComment comment,
        ActualTargetResult actualTarget,
        MultiLabelMetricSummary metrics,
        List<BenchmarkFailure> failures)
    {
        if (expected.Count == 0 && actual.Count == 0)
        {
            return;
        }

        metrics.Add(expected, actual);
        if (expected.SetEquals(actual))
        {
            return;
        }

        var missing = expected.Except(actual, StringComparer.OrdinalIgnoreCase).ToList();
        var extra = actual.Except(expected, StringComparer.OrdinalIgnoreCase).ToList();
        var category = missing.Count > 0 && extra.Count > 0
            ? $"{prefix}:Mismatch"
            : missing.Count > 0
                ? $"{prefix}:Missing"
                : $"{prefix}:Extra";

        failures.Add(new BenchmarkFailure
        {
            Category = category,
            ThreadName = fixture.Thread.Name,
            CommentId = comment.Id,
            Body = comment.Body,
            Expected = $"{expectedTarget.Player} {JoinOrNone(expected)}",
            Actual = $"{actualTarget.Player} {JoinOrNone(actual)} (legacy intent: {actualTarget.Intent})",
            ContextRequirement = comment.Expected.ContextRequirement,
            Notes = expectedTarget.Notes ?? comment.Notes,
            Diagnostics = actualTarget.Diagnostics
        });
    }

    private static bool IsContextExcludedFromFantasyOutlook(BenchmarkComment comment)
    {
        return string.Equals(comment.Expected.ContextRequirement, BenchmarkContextRequirements.GameEventContext, StringComparison.OrdinalIgnoreCase)
               || string.Equals(comment.Expected.ContextRequirement, BenchmarkContextRequirements.Ambiguous, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<string> NormalizeFantasyActions(BenchmarkExpectedTarget target)
    {
        return target.FantasyActions
            .Concat(target.Intents.Where(BenchmarkFantasyActions.All.Contains))
            .Where(BenchmarkFantasyActions.All.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<string> NormalizeDiscussionSignals(BenchmarkExpectedTarget target)
    {
        return target.DiscussionSignals
            .Concat(target.Intents.Where(BenchmarkDiscussionSignals.All.Contains))
            .Where(BenchmarkDiscussionSignals.All.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<string> ProjectAction(string intent)
    {
        return BenchmarkFantasyActions.All.Contains(intent)
            ? new HashSet<string>([intent], StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<string> ProjectSignal(string intent)
    {
        return BenchmarkDiscussionSignals.All.Contains(intent)
            ? new HashSet<string>([intent], StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

    private static IEnumerable<BenchmarkFailure> FailuresFor(
        IReadOnlyList<BenchmarkFailure> failures,
        string prefix)
    {
        return failures.Where(failure =>
            failure.Category.StartsWith($"{prefix}:", StringComparison.OrdinalIgnoreCase));
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
                   || body.Contains("killing me", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("destroyed me", StringComparison.OrdinalIgnoreCase)
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
                   || body.Contains("i chose", StringComparison.OrdinalIgnoreCase)
                ? "Lineup regret or ownership language missed"
                : "Implicit ownership missed";
        }

        return failure.Actual.Contains(TargetPerspectives.Unknown, StringComparison.OrdinalIgnoreCase)
            ? "Incorrectly classified Unknown"
            : "Other perspective mismatch";
    }

    private static string CategorizeSetFailure(BenchmarkFailure failure)
    {
        return failure.Category.Split(':', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "Mismatch";
    }

    private static string CategorizeSemanticFailure(BenchmarkFailure failure)
    {
        if (failure.Category.Contains("OpponentPerspective", StringComparison.OrdinalIgnoreCase))
        {
            return "OpponentThreat";
        }

        foreach (var reason in new[]
                 {
                     "Sarcasm",
                     "Comparison",
                     "DraftRegret",
                     "LineupRegret",
                     "Injury",
                     "QuarterbackPlay",
                     "OffensiveLine",
                     "CoachingPlaycalling",
                     "RoleCompetition",
                     "OpponentDefense",
                     "CalledBackPlay",
                     "UsageOpportunity",
                     "FantasyProduction",
                     "Performance",
                     "AntiPanic",
                     "WaiverAdd",
                     "PlayerVsSituation"
                 })
        {
            if (failure.Category.Contains(reason, StringComparison.OrdinalIgnoreCase))
            {
                return reason;
            }
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
            || failure.Body.Contains("ass", StringComparison.OrdinalIgnoreCase)
            || failure.Body.Contains("truth", StringComparison.OrdinalIgnoreCase))
        {
            return "Slang";
        }

        return "Other";
    }

    private static void AppendConfusionMatrix(
        StringBuilder builder,
        string title,
        IReadOnlyDictionary<string, Dictionary<string, int>> matrix)
    {
        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        builder.AppendLine("| Expected | Actual Negative | Actual Neutral | Actual Positive |");
        builder.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var expected in new[] { BenchmarkDirections.Negative, BenchmarkDirections.Neutral, BenchmarkDirections.Positive })
        {
            var row = matrix[expected];
            builder.AppendLine($"| {expected} | {row[BenchmarkDirections.Negative]} | {row[BenchmarkDirections.Neutral]} | {row[BenchmarkDirections.Positive]} |");
        }
    }

    private static void AppendDivergenceSection(StringBuilder builder, BenchmarkReport report)
    {
        builder.AppendLine();
        builder.AppendLine("## Player Evaluation / Fantasy Outlook Divergence");
        builder.AppendLine();
        var divergenceRate = report.PlayerEvaluationOutlookComparableCount == 0
            ? 0
            : Math.Round(report.PlayerEvaluationOutlookDivergenceCount * 100.0 / report.PlayerEvaluationOutlookComparableCount, 1);
        builder.AppendLine($"Divergences: {report.PlayerEvaluationOutlookDivergenceCount}/{report.PlayerEvaluationOutlookComparableCount} ({divergenceRate:0.#}%).");
        foreach (var example in report.PlayerEvaluationOutlookDivergences)
        {
            builder.AppendLine($"- {example.CommentId} / {example.Player}: outlook={example.FantasyOutlook}, playerEvaluation={example.PlayerEvaluation}, reasons={example.Reasons}");
            builder.AppendLine($"  - {Trim(example.Body, 170)}");
        }
    }

    private static void AppendCoOccurrenceSection(StringBuilder builder, BenchmarkReport report)
    {
        builder.AppendLine();
        builder.AppendLine("## Action / Signal Co-Occurrence");
        builder.AppendLine();
        builder.AppendLine($"Targets with both fantasy action and discussion signal labels: {report.ActionSignalCoOccurrenceCount}.");
        foreach (var example in report.ActionSignalCoOccurrences)
        {
            builder.AppendLine($"- {example.CommentId} / {example.Player}: actions={example.FantasyActions}; signals={example.DiscussionSignals}");
            builder.AppendLine($"  - {Trim(example.Body, 170)}");
        }
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

    private static void AppendDebatableSection(
        StringBuilder builder,
        IReadOnlyList<BenchmarkFailure> audits)
    {
        builder.AppendLine();
        builder.AppendLine("## Debatable Annotation Audit");
        builder.AppendLine();
        foreach (var audit in audits)
        {
            builder.AppendLine($"- {audit.CommentId} / {audit.Expected}");
            builder.AppendLine($"  - Comment: {Trim(audit.Body, 170)}");
            builder.AppendLine($"  - Alternative plausible labels: {audit.Actual}");
            if (!string.IsNullOrWhiteSpace(audit.Notes))
            {
                builder.AppendLine($"  - Reason: {audit.Notes}");
            }
        }
    }

    private static void AppendRepresentativeFailures(
        StringBuilder builder,
        IReadOnlyList<BenchmarkFailure> failures)
    {
        builder.AppendLine();
        builder.AppendLine("## Representative Failures");
        builder.AppendLine();
        foreach (var failure in failures)
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
    }

    private static string JoinOrNone(IEnumerable<string> values)
    {
        var valueList = values.ToList();
        return valueList.Count == 0 ? "none" : string.Join("/", valueList);
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
