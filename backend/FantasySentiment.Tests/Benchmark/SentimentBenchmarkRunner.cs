using System.Text;
using System.Text.RegularExpressions;
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
    private const string SemanticProfile = "fantasy-semantics-v4";

    private readonly BenchmarkCorpus _corpus;
    private readonly CommentTargetResolver _targetResolver;
    private readonly TargetContextBuilder _targetContextBuilder;
    private readonly ThreadContextBuilder _threadContextBuilder;
    private readonly VaderSentimentAnalyzer _sentimentAnalyzer;
    private readonly FantasyIntentClassifier _intentClassifier;
    private readonly SentimentScoringPolicy _scoringPolicy = new();

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
        var targetEvaluations = new List<BenchmarkTargetEvaluation>();
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
                targetEvaluations,
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
        report.AllFailures = failures;
        report.TargetEvaluations = targetEvaluations;
        report.SplitPrecisionSummaries = BuildSplitPrecisionSummaries(report.ByThread, targetEvaluations);
        report.GateExclusionSummaries = BuildGateExclusionSummaries(targetEvaluations);
        report.SentimentGateSummaries = BuildSentimentGateSummaries(targetEvaluations);
        report.ConfidenceCalibration = BuildConfidenceCalibration(targetEvaluations);
        report.ConfidenceValues = BuildConfidenceValues(targetEvaluations);
        report.HighConfidenceErrors = BuildHighConfidenceErrors(targetEvaluations).Take(20).ToList();
        report.FalseAbstentions = BuildAbstentionExamples(targetEvaluations, falseAbstention: true).Take(20).ToList();
        report.GoodAbstentions = BuildAbstentionExamples(targetEvaluations, falseAbstention: false).Take(20).ToList();

        return report;
    }

    public string ToMarkdown(BenchmarkReport report)
    {
        return ToMarkdown(
            report,
            "Sentiment Benchmark",
            [
                "Scope: canonical full labeled model-health report.",
                "Loaded splits: Development, Validation, EvaluationB when present."
            ]);
    }

    public string ToEvaluationBMarkdown(BenchmarkReport report)
    {
        return ToMarkdown(
            report,
            "Evaluation B Sentiment Benchmark",
            [
                "DatasetSplit: EvaluationB",
                "Source: audited Week 1 Sunday Afternoon Game Thread",
                $"Semantic profile: {SemanticProfile}",
                "Note: formerly audited Holdout B."
            ]);
    }

    private string ToMarkdown(
        BenchmarkReport report,
        string title,
        IReadOnlyList<string> extraHeaderLines)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {title}");
        builder.AppendLine();
        builder.AppendLine($"Generated: {report.GeneratedAtUtc:O}");
        builder.AppendLine($"Dataset splits: {string.Join(", ", report.ByThread.Select(thread => thread.DatasetSplit).Distinct(StringComparer.OrdinalIgnoreCase))}");
        builder.AppendLine($"Semantic profile: {SemanticProfile}");
        builder.AppendLine($"Analyzer: {report.Analyzer} (frozen)");
        builder.AppendLine($"Target resolver: {report.TargetResolver}");
        foreach (var line in extraHeaderLines)
        {
            builder.AppendLine(line);
        }

        builder.AppendLine($"Fixtures: {report.FixtureCount}");
        builder.AppendLine($"Comments: {report.TotalComments}");
        builder.AppendLine($"Expected targets: {report.TotalExpectedTargets}");
        builder.AppendLine($"Comments with no expected targets: {report.CommentsWithoutExpectedTargets}");
        builder.AppendLine($"Scorable fantasy-outlook targets: {report.ScorableFantasyOutlookTargets}");
        builder.AppendLine($"Fantasy-outlook targets excluded by context: {report.FantasyOutlookExcludedByContext}");
        builder.AppendLine();
        AppendDatasetSplitSection(builder, report);
        AppendSentimentGateSection(builder, report);
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
        builder.AppendLine("Fantasy actions and discussion signals are evaluated as separate target-scoped semantic sets. Hype and Panic are no longer benchmark discussion-signal labels; bullish and bearish language is represented through FantasyOutlook.");
        builder.AppendLine();
        builder.AppendLine("## Old Contract Comparison");
        builder.AppendLine();
        builder.AppendLine($"Previous benchmark interpretation: comments={PreviousCommentCount}, expectedTargets={PreviousExpectedTargetCount}, targetF1={PreviousTargetF1:0.###}, sentimentAccuracy={PreviousFantasyOutlookAccuracy:0.###}, perspectiveAccuracy={PreviousPerspectiveAccuracy:0.###}, singleIntentAccuracy={PreviousSingleIntentAccuracy:0.###}.");
        builder.AppendLine($"This report evaluates the current {SemanticProfile} production semantics on the loaded dataset splits. Metric changes may reflect both annotation corrections and production semantic tuning.");
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
        AppendGateExampleSection(builder, "High-Confidence Sentiment Errors", report.HighConfidenceErrors);
        AppendGateExampleSection(builder, "False Abstentions", report.FalseAbstentions);
        AppendGateExampleSection(builder, "Good Abstentions", report.GoodAbstentions);
        AppendDebatableSection(builder, report.DebatableLabels);
        AppendRepresentativeFailures(builder, report.RepresentativeFailures);
        AppendValidationDiagnostics(builder, report);
        AppendDatasetDiagnostics(builder, report, "EvaluationB", "Evaluation B");

        return builder.ToString();
    }

    private async Task<ThreadBenchmarkSummary> EvaluateFixtureAsync(
        BenchmarkFixture fixture,
        List<BenchmarkFailure> failures,
        List<BenchmarkFailure> labelAudits,
        List<SemanticDivergenceExample> divergences,
        List<ActionSignalCoOccurrenceExample> coOccurrences,
        List<BenchmarkTargetEvaluation> targetEvaluations,
        CancellationToken cancellationToken)
    {
        var commentsById = fixture.Comments.ToDictionary(
            comment => comment.Id,
            StringComparer.OrdinalIgnoreCase);
        var resolutionByCommentId = new Dictionary<string, CommentTargetResolution>(StringComparer.OrdinalIgnoreCase);
        var processingCommentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var players = PlayersForFixture(fixture);
        var summary = new ThreadBenchmarkSummary
        {
            DatasetSplit = fixture.Thread.DatasetSplit,
            ThreadName = fixture.Thread.Name,
            CommentCount = fixture.Comments.Count,
            ExpectedTargetCount = fixture.Comments.Sum(comment => comment.Expected.Targets.Count),
            CommentsWithoutExpectedTargets = fixture.Comments.Count(comment => comment.Expected.Targets.Count == 0)
        };
        summary.ContextRequirementCounts = fixture.Comments
            .GroupBy(comment => comment.Expected.ContextRequirement, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        for (var commentIndex = 0; commentIndex < fixture.Comments.Count; commentIndex++)
        {
            var comment = fixture.Comments[commentIndex];
            var resolution = ResolveCommentTargets(
                fixture,
                comment,
                players,
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
                coOccurrences,
                targetEvaluations,
                commentIndex + 1);
        }

        return summary;
    }

    private CommentTargetResolution ResolveCommentTargets(
        BenchmarkFixture fixture,
        BenchmarkComment comment,
        IReadOnlyList<Player> players,
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
                players,
                commentsById,
                resolutionByCommentId,
                processingCommentIds).Targets;
        }

        var resolution = _targetResolver.Resolve(new CommentTargetResolutionRequest(
            comment.Id,
            comment.Body,
            fixture.Thread.Name,
            players,
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
            var semanticClassification = _intentClassifier.ClassifyAll(
                comment.Body,
                resolvedContext.TargetText,
                match);

            actual.Add(new ActualTargetResult(
                target.PlayerName,
                BenchmarkDirections.FromScore(sentiment.FinalSentimentScore),
                sentiment.FinalSentimentScore,
                resolvedContext.Perspective,
                semanticClassification.LegacyIntent,
                semanticClassification.FantasyActions.ToHashSet(StringComparer.OrdinalIgnoreCase),
                semanticClassification.DiscussionSignals.ToHashSet(StringComparer.OrdinalIgnoreCase),
                target.AttributionMethod,
                target.Confidence,
                sentiment.SemanticConfidence,
                resolvedContext.TargetText,
                sentiment.Diagnostics));
        }

        return actual;
    }

    private IReadOnlyList<Player> PlayersForFixture(BenchmarkFixture fixture)
    {
        if (fixture.Players.Count == 0)
        {
            return _corpus.Players;
        }

        var id = 1;
        return fixture.Players
            .OrderBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .Select(player => new Player
            {
                Id = id++,
                Name = player.Name,
                Team = player.Team,
                Position = player.Position,
                ExternalId = player.ExternalId,
                IsActive = true,
                Aliases = player.Aliases
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(alias => new PlayerAlias { Alias = alias })
                    .ToList()
            })
            .ToList();
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
                DatasetSplit = fixture.Thread.DatasetSplit,
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
                DatasetSplit = fixture.Thread.DatasetSplit,
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
        List<ActionSignalCoOccurrenceExample> coOccurrences,
        List<BenchmarkTargetEvaluation> targetEvaluations,
        int commentIndex)
    {
        foreach (var expectedTarget in comment.Expected.Targets)
        {
            ScoreSemanticCoverage(fixture, comment, expectedTarget, summary, labelAudits, divergences, coOccurrences);

            var actual = actualTargets.FirstOrDefault(target =>
                string.Equals(target.Player, expectedTarget.Player, StringComparison.OrdinalIgnoreCase));
            if (actual is null)
            {
                AddTargetEvaluation(fixture, comment, commentIndex, expectedTarget, null, targetEvaluations);
                continue;
            }

            AddTargetEvaluation(fixture, comment, commentIndex, expectedTarget, actual, targetEvaluations);
            ScoreFantasyOutlook(fixture, comment, expectedTarget, actual, summary, failures);
            ScorePlayerEvaluation(fixture, comment, expectedTarget, actual, summary, failures);
            ScorePerspective(fixture, comment, expectedTarget, actual, summary, failures);
            ScoreProjectedSet(
                "FantasyAction",
                expectedTarget,
                NormalizeFantasyActions(expectedTarget),
                actual.FantasyActions,
                fixture,
                comment,
                actual,
                summary.FantasyActionMetrics,
                failures);
            ScoreProjectedSet(
                "DiscussionSignal",
                expectedTarget,
                NormalizeDiscussionSignals(expectedTarget),
                actual.DiscussionSignals,
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
                DatasetSplit = fixture.Thread.DatasetSplit,
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

    private static void AddTargetEvaluation(
        BenchmarkFixture fixture,
        BenchmarkComment comment,
        int commentIndex,
        BenchmarkExpectedTarget expectedTarget,
        ActualTargetResult? actual,
        List<BenchmarkTargetEvaluation> targetEvaluations)
    {
        targetEvaluations.Add(new BenchmarkTargetEvaluation
        {
            DatasetSplit = fixture.Thread.DatasetSplit,
            ThreadName = fixture.Thread.Name,
            CommentIndex = commentIndex,
            CommentId = comment.Id,
            Body = comment.Body,
            Player = expectedTarget.Player,
            ContextRequirement = comment.Expected.ContextRequirement,
            ExpectedFantasyOutlook = expectedTarget.FantasyOutlook,
            ExpectedPlayerEvaluation = expectedTarget.PlayerEvaluation,
            ExpectedDirection = BenchmarkDirections.FromExpectedLabel(expectedTarget.FantasyOutlook)
                                ?? BenchmarkDirections.FromExpectedLabel(expectedTarget.Sentiment)
                                ?? BenchmarkFantasyLabels.Unknown,
            ExpectedPerspective = expectedTarget.Perspective,
            ExpectedActions = JoinOrNone(NormalizeFantasyActions(expectedTarget)),
            ExpectedSignals = JoinOrNone(NormalizeDiscussionSignals(expectedTarget)),
            ActualDirection = actual?.SentimentDirection ?? "Missing",
            ActualScore = actual?.SentimentScore,
            ActualPerspective = actual?.Perspective,
            ActualIntent = actual?.Intent,
            ActualActions = actual is null ? "Missing" : JoinOrNone(actual.FantasyActions),
            ActualSignals = actual is null ? "Missing" : JoinOrNone(actual.DiscussionSignals),
            AttributionMethod = actual?.AttributionMethod,
            TargetConfidence = actual?.TargetConfidence,
            SemanticConfidence = actual?.SemanticConfidence ?? ExtractSemanticConfidence(actual?.Diagnostics),
            SemanticConfidenceBand = SemanticConfidenceBand(actual?.SemanticConfidence ?? ExtractSemanticConfidence(actual?.Diagnostics)),
            TargetText = actual?.TargetText,
            Diagnostics = actual?.Diagnostics,
            Reasons = string.Join("/", expectedTarget.Reasons),
            IsDebatable = expectedTarget.IsDebatable || expectedTarget.AlternativeLabels.Count > 0,
            Notes = expectedTarget.Notes ?? comment.Notes
        });
    }

    private static double? ExtractSemanticConfidence(string? diagnostics)
    {
        if (string.IsNullOrWhiteSpace(diagnostics))
        {
            return null;
        }

        var match = Regex.Match(
            diagnostics,
            @"semanticConfidence=(?<value>-?\d+(?:\.\d+)?)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return match.Success && double.TryParse(match.Groups["value"].Value, out var value)
            ? value
            : null;
    }

    private static string SemanticConfidenceBand(double? confidence)
    {
        return confidence switch
        {
            >= 0.75 => "High",
            >= 0.45 => "Medium",
            null => "Missing",
            _ => "Low"
        };
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
                DatasetSplit = fixture.Thread.DatasetSplit,
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
                DatasetSplit = fixture.Thread.DatasetSplit,
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
                DatasetSplit = fixture.Thread.DatasetSplit,
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
            DatasetSplit = fixture.Thread.DatasetSplit,
            Category = category,
            ThreadName = fixture.Thread.Name,
            CommentId = comment.Id,
            Body = comment.Body,
            Expected = $"{expectedTarget.Player} {JoinOrNone(expected)}",
            Actual = $"{actualTarget.Player} {JoinOrNone(actual)}",
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

    private static ThreadBenchmarkSummary AggregateThreads(
        string datasetSplit,
        IEnumerable<ThreadBenchmarkSummary> threads)
    {
        var aggregate = new ThreadBenchmarkSummary
        {
            DatasetSplit = datasetSplit,
            ThreadName = datasetSplit
        };

        foreach (var thread in threads)
        {
            aggregate.CommentCount += thread.CommentCount;
            aggregate.ExpectedTargetCount += thread.ExpectedTargetCount;
            aggregate.CommentsWithoutExpectedTargets += thread.CommentsWithoutExpectedTargets;
            aggregate.FantasyOutlookExcludedByContext += thread.FantasyOutlookExcludedByContext;
            aggregate.FantasyOutlookLabeledTargets += thread.FantasyOutlookLabeledTargets;
            aggregate.PlayerEvaluationLabeledTargets += thread.PlayerEvaluationLabeledTargets;
            aggregate.PerspectiveLabeledTargets += thread.PerspectiveLabeledTargets;
            aggregate.FantasyActionLabeledTargets += thread.FantasyActionLabeledTargets;
            aggregate.FantasyActionLabelCount += thread.FantasyActionLabelCount;
            aggregate.DiscussionSignalLabeledTargets += thread.DiscussionSignalLabeledTargets;
            aggregate.DiscussionSignalLabelCount += thread.DiscussionSignalLabelCount;
            aggregate.TargetMetrics.Add(thread.TargetMetrics);
            aggregate.FantasyOutlookMetrics.Add(thread.FantasyOutlookMetrics);
            aggregate.PlayerEvaluationMetrics.Add(thread.PlayerEvaluationMetrics);
            aggregate.PerspectiveMetrics.Add(thread.PerspectiveMetrics);
            aggregate.FantasyActionMetrics.Add(thread.FantasyActionMetrics);
            aggregate.DiscussionSignalMetrics.Add(thread.DiscussionSignalMetrics);
            AddConfusionMatrix(aggregate.FantasyOutlookConfusionMatrix, thread.FantasyOutlookConfusionMatrix);
            AddConfusionMatrix(aggregate.PlayerEvaluationConfusionMatrix, thread.PlayerEvaluationConfusionMatrix);
            AddCounts(aggregate.ContextRequirementCounts, thread.ContextRequirementCounts);
        }

        EnsureContextKeys(aggregate.ContextRequirementCounts);
        return aggregate;
    }

    private List<SplitPrecisionSummary> BuildSplitPrecisionSummaries(
        IReadOnlyList<ThreadBenchmarkSummary> threads,
        IReadOnlyList<BenchmarkTargetEvaluation> targetEvaluations)
    {
        return threads
            .GroupBy(thread => thread.DatasetSplit, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var split = AggregateThreads(group.Key, group);
                var scorable = targetEvaluations
                    .Where(evaluation => string.Equals(evaluation.DatasetSplit, group.Key, StringComparison.OrdinalIgnoreCase))
                    .Where(IsScorableEvaluation)
                    .ToList();
                var actual = scorable.Where(HasActualDirection).ToList();
                var gated = actual.Where(PassesProductionGate).ToList();
                var highConfidence = actual
                    .Where(evaluation =>
                        evaluation.SemanticConfidence >= SentimentScoringPolicy.StrictDiagnosticSemanticConfidence
                        && evaluation.TargetConfidence >= SentimentScoringPolicy.MinimumTargetConfidence)
                    .ToList();

                return new SplitPrecisionSummary
                {
                    DatasetSplit = group.Key,
                    CommentCount = split.CommentCount,
                    ExpectedTargetCount = split.ExpectedTargetCount,
                    TargetF1 = split.TargetMetrics.F1,
                    RawFantasyOutlookAccuracy = split.FantasyOutlookMetrics.Accuracy,
                    RawFantasyOutlookCorrect = split.FantasyOutlookMetrics.Correct,
                    RawFantasyOutlookTotal = split.FantasyOutlookMetrics.Total,
                    GatedFantasyOutlookAccuracy = Accuracy(gated),
                    GatedFantasyOutlookCorrect = gated.Count(IsDirectionCorrect),
                    GatedFantasyOutlookTotal = gated.Count,
                    ProductionCoverage = Coverage(gated.Count, scorable.Count),
                    PlayerEvaluationAccuracy = split.PlayerEvaluationMetrics.Accuracy,
                    PerspectiveAccuracy = split.PerspectiveMetrics.Accuracy,
                    FantasyActionF1 = split.FantasyActionMetrics.F1,
                    DiscussionSignalF1 = split.DiscussionSignalMetrics.F1,
                    HighConfidenceAccuracy = Accuracy(highConfidence),
                    HighConfidenceCorrect = highConfidence.Count(IsDirectionCorrect),
                    HighConfidenceTotal = highConfidence.Count,
                    HighConfidenceCoverage = Coverage(highConfidence.Count, scorable.Count)
                };
            })
            .ToList();
    }

    private List<GateExclusionSummary> BuildGateExclusionSummaries(
        IReadOnlyList<BenchmarkTargetEvaluation> targetEvaluations)
    {
        return targetEvaluations
            .Where(IsScorableEvaluation)
            .Where(HasActualDirection)
            .GroupBy(evaluation => evaluation.DatasetSplit, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var summary = new GateExclusionSummary
                {
                    DatasetSplit = group.Key,
                    ActualScorableMentions = group.Count()
                };

                foreach (var evaluation in group)
                {
                    if (!evaluation.SemanticConfidence.HasValue || !evaluation.TargetConfidence.HasValue)
                    {
                        summary.MissingConfidence++;
                        continue;
                    }

                    var semanticFailed = evaluation.SemanticConfidence.Value < SentimentScoringPolicy.MinimumSemanticConfidence;
                    var targetFailed = evaluation.TargetConfidence.Value < SentimentScoringPolicy.MinimumTargetConfidence;
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
            })
            .ToList();
    }

    private List<SentimentGateSummary> BuildSentimentGateSummaries(
        IReadOnlyList<BenchmarkTargetEvaluation> targetEvaluations)
    {
        var summaries = new List<SentimentGateSummary>();
        foreach (var group in targetEvaluations
                     .Where(IsScorableEvaluation)
                     .GroupBy(evaluation => evaluation.DatasetSplit, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var evaluations = group.ToList();
            var actualEvaluations = evaluations
                .Where(HasActualDirection)
                .ToList();
            var gated = actualEvaluations
                .Where(PassesProductionGate)
                .ToList();
            var abstained = actualEvaluations
                .Where(evaluation => !PassesProductionGate(evaluation))
                .ToList();

            summaries.Add(new SentimentGateSummary
            {
                DatasetSplit = group.Key,
                Gate = "Raw",
                TotalScorable = evaluations.Count,
                Evaluated = actualEvaluations.Count,
                Correct = actualEvaluations.Count(IsDirectionCorrect),
                Abstained = 0
            });

            summaries.Add(new SentimentGateSummary
            {
                DatasetSplit = group.Key,
                Gate = $"Production >= {SentimentScoringPolicy.MinimumSemanticConfidence:0.##}",
                TotalScorable = evaluations.Count,
                Evaluated = gated.Count,
                Correct = gated.Count(IsDirectionCorrect),
                Abstained = abstained.Count,
                GoodAbstentions = abstained.Count(evaluation => !IsDirectionCorrect(evaluation)),
                FalseAbstentions = abstained.Count(IsDirectionCorrect)
            });
        }

        return summaries;
    }

    private static List<ConfidenceCalibrationSummary> BuildConfidenceCalibration(
        IReadOnlyList<BenchmarkTargetEvaluation> targetEvaluations)
    {
        return targetEvaluations
            .Where(IsScorableEvaluation)
            .Where(HasActualDirection)
            .GroupBy(
                evaluation => new
                {
                    evaluation.DatasetSplit,
                    Band = ConfidenceBin(evaluation.SemanticConfidence)
                })
            .OrderBy(group => group.Key.DatasetSplit, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => ConfidenceBinOrder(group.Key.Band))
            .Select(group => new ConfidenceCalibrationSummary
            {
                DatasetSplit = group.Key.DatasetSplit,
                Band = group.Key.Band,
                Count = group.Count(),
                Correct = group.Count(IsDirectionCorrect)
            })
            .ToList();
    }

    private static List<ConfidenceValueSummary> BuildConfidenceValues(
        IReadOnlyList<BenchmarkTargetEvaluation> targetEvaluations)
    {
        return targetEvaluations
            .Where(IsScorableEvaluation)
            .Where(HasActualDirection)
            .Where(evaluation => evaluation.SemanticConfidence.HasValue)
            .GroupBy(
                evaluation => new
                {
                    evaluation.DatasetSplit,
                    Confidence = Math.Round(evaluation.SemanticConfidence!.Value, 3)
                })
            .OrderBy(group => group.Key.DatasetSplit, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(group => group.Count())
            .ThenBy(group => group.Key.Confidence)
            .Select(group => new ConfidenceValueSummary
            {
                DatasetSplit = group.Key.DatasetSplit,
                SemanticConfidence = group.Key.Confidence,
                Count = group.Count(),
                Correct = group.Count(IsDirectionCorrect)
            })
            .ToList();
    }

    private List<BenchmarkFailure> BuildHighConfidenceErrors(
        IReadOnlyList<BenchmarkTargetEvaluation> targetEvaluations)
    {
        return targetEvaluations
            .Where(IsScorableEvaluation)
            .Where(HasActualDirection)
            .Where(evaluation => evaluation.SemanticConfidence >= SentimentScoringPolicy.StrictDiagnosticSemanticConfidence)
            .Where(evaluation => !IsDirectionCorrect(evaluation))
            .Select(evaluation => ToGateFailure(evaluation, "HighConfidenceError"))
            .ToList();
    }

    private List<BenchmarkFailure> BuildAbstentionExamples(
        IReadOnlyList<BenchmarkTargetEvaluation> targetEvaluations,
        bool falseAbstention)
    {
        return targetEvaluations
            .Where(IsScorableEvaluation)
            .Where(HasActualDirection)
            .Where(evaluation => !PassesProductionGate(evaluation))
            .Where(evaluation => IsDirectionCorrect(evaluation) == falseAbstention)
            .Select(evaluation => ToGateFailure(
                evaluation,
                falseAbstention ? "FalseAbstention" : "GoodAbstention"))
            .ToList();
    }

    private bool PassesProductionGate(BenchmarkTargetEvaluation evaluation)
    {
        return evaluation.SemanticConfidence.HasValue
               && evaluation.TargetConfidence.HasValue
               && _scoringPolicy.Evaluate(
                   evaluation.SemanticConfidence.Value,
                   evaluation.TargetConfidence.Value).IsScored;
    }

    private static bool HasActualDirection(BenchmarkTargetEvaluation evaluation)
    {
        return evaluation.ActualScore.HasValue
               && (evaluation.ActualDirection.Equals(BenchmarkDirections.Negative, StringComparison.OrdinalIgnoreCase)
                   || evaluation.ActualDirection.Equals(BenchmarkDirections.Neutral, StringComparison.OrdinalIgnoreCase)
                   || evaluation.ActualDirection.Equals(BenchmarkDirections.Positive, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDirectionCorrect(BenchmarkTargetEvaluation evaluation)
    {
        return string.Equals(
            evaluation.ExpectedDirection,
            evaluation.ActualDirection,
            StringComparison.OrdinalIgnoreCase);
    }

    private static double Accuracy(IReadOnlyList<BenchmarkTargetEvaluation> evaluations)
    {
        return evaluations.Count == 0
            ? 0
            : Math.Round(evaluations.Count(IsDirectionCorrect) / (double)evaluations.Count, 3);
    }

    private static double Coverage(int evaluated, int total)
    {
        return total == 0 ? 0 : Math.Round(evaluated / (double)total, 3);
    }

    private static BenchmarkFailure ToGateFailure(BenchmarkTargetEvaluation evaluation, string category)
    {
        return new BenchmarkFailure
        {
            DatasetSplit = evaluation.DatasetSplit,
            Category = category,
            ThreadName = evaluation.ThreadName,
            CommentId = evaluation.CommentId,
            Body = evaluation.Body,
            Expected = $"{evaluation.Player} {evaluation.ExpectedDirection}",
            Actual = $"{evaluation.Player} {evaluation.ActualDirection} ({evaluation.ActualScore:0.###})",
            ContextRequirement = evaluation.ContextRequirement,
            Notes = evaluation.Notes,
            Diagnostics = evaluation.Diagnostics
        };
    }

    private static string ConfidenceBin(double? confidence)
    {
        return confidence switch
        {
            null => "Missing",
            < 0.25 => "<0.25",
            < 0.45 => "0.25-<0.45",
            < 0.75 => "0.45-<0.75",
            _ => ">=0.75"
        };
    }

    private static int ConfidenceBinOrder(string bin)
    {
        return bin switch
        {
            "<0.25" => 0,
            "0.25-<0.45" => 1,
            "0.45-<0.75" => 2,
            ">=0.75" => 3,
            _ => 4
        };
    }

    private static void AppendDatasetSplitSection(StringBuilder builder, BenchmarkReport report)
    {
        builder.AppendLine("## Dataset Split Summary");
        builder.AppendLine();
        builder.AppendLine("| Split | Comments | Expected targets | Target F1 | Raw FantasyOutlook | Gated FantasyOutlook | Coverage | PlayerEvaluation | Perspective | Actions F1 | Signals F1 | >=0.75 Acc | >=0.75 Coverage |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var split in report.SplitPrecisionSummaries)
        {
            builder.AppendLine(
                $"| {split.DatasetSplit} | {split.CommentCount} | {split.ExpectedTargetCount} | {split.TargetF1:0.###} | {split.RawFantasyOutlookAccuracy:0.###} ({split.RawFantasyOutlookCorrect}/{split.RawFantasyOutlookTotal}) | {split.GatedFantasyOutlookAccuracy:0.###} ({split.GatedFantasyOutlookCorrect}/{split.GatedFantasyOutlookTotal}) | {split.ProductionCoverage:0.###} | {split.PlayerEvaluationAccuracy:0.###} | {split.PerspectiveAccuracy:0.###} | {split.FantasyActionF1:0.###} | {split.DiscussionSignalF1:0.###} | {split.HighConfidenceAccuracy:0.###} ({split.HighConfidenceCorrect}/{split.HighConfidenceTotal}) | {split.HighConfidenceCoverage:0.###} |");
        }
    }

    private static void AppendSentimentGateSection(StringBuilder builder, BenchmarkReport report)
    {
        builder.AppendLine();
        builder.AppendLine("## Precision Gate Summary");
        builder.AppendLine();
        builder.AppendLine("| Split | Gate | Coverage | Accuracy | Correct/Scored | Abstained | Good abstentions | False abstentions |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var summary in report.SentimentGateSummaries)
        {
            builder.AppendLine(
                $"| {summary.DatasetSplit} | {summary.Gate} | {summary.Coverage:0.###} | {summary.Accuracy:0.###} | {summary.Correct}/{summary.Evaluated} | {summary.Abstained} | {summary.GoodAbstentions} | {summary.FalseAbstentions} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Production Gate Exclusions");
        builder.AppendLine();
        builder.AppendLine("| Split | Actual scorable mentions | Scored | Semantic confidence only | Target confidence only | Failed both | Missing confidence |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var summary in report.GateExclusionSummaries)
        {
            builder.AppendLine(
                $"| {summary.DatasetSplit} | {summary.ActualScorableMentions} | {summary.Scored} | {summary.SemanticConfidenceOnly} | {summary.TargetConfidenceOnly} | {summary.FailedBoth} | {summary.MissingConfidence} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Confidence Calibration");
        builder.AppendLine();
        builder.AppendLine("| Split | Semantic confidence bin | Count | Accuracy | Correct |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: |");
        foreach (var bin in report.ConfidenceCalibration)
        {
            builder.AppendLine(
                $"| {bin.DatasetSplit} | {bin.Band} | {bin.Count} | {bin.Accuracy:0.###} | {bin.Correct} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Common Semantic Confidence Values");
        builder.AppendLine();
        builder.AppendLine("| Split | Confidence | Count | Accuracy | Correct |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: |");
        foreach (var value in report.ConfidenceValues
                     .GroupBy(item => item.DatasetSplit, StringComparer.OrdinalIgnoreCase)
                     .SelectMany(group => group.Take(8)))
        {
            builder.AppendLine(
                $"| {value.DatasetSplit} | {value.SemanticConfidence:0.###} | {value.Count} | {value.Accuracy:0.###} | {value.Correct} |");
        }
    }

    private static void AppendGateExampleSection(
        StringBuilder builder,
        string title,
        IReadOnlyList<BenchmarkFailure> examples)
    {
        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        if (examples.Count == 0)
        {
            builder.AppendLine("- none");
            return;
        }

        foreach (var example in examples.Take(12))
        {
            builder.AppendLine($"- [{example.DatasetSplit}] {example.CommentId}: {Trim(example.Body, 170)}");
            builder.AppendLine($"  - Expected: {example.Expected}");
            builder.AppendLine($"  - Actual: {example.Actual}");
        }
    }

    private static void AppendValidationDiagnostics(StringBuilder builder, BenchmarkReport report)
    {
        var validationThreads = report.ByThread
            .Where(thread => string.Equals(thread.DatasetSplit, "Validation", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (validationThreads.Count == 0)
        {
            return;
        }

        var validation = AggregateThreads("Validation", validationThreads);
        var validationFailures = report.AllFailures
            .Where(failure => string.Equals(failure.DatasetSplit, "Validation", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var validationEvaluations = report.TargetEvaluations
            .Where(evaluation => string.Equals(evaluation.DatasetSplit, "Validation", StringComparison.OrdinalIgnoreCase))
            .ToList();

        AppendConfusionMatrix(builder, "Validation Fantasy Outlook Confusion Matrix", validation.FantasyOutlookConfusionMatrix);
        AppendValidationPlayerSummary(builder, validationEvaluations, validationFailures);
        AppendLondonTrajectory(builder, validationEvaluations);
        AppendValidationFailureCategories(builder, validationFailures);
        AppendValidationRepresentativeFailures(builder, validationFailures);
        AppendUnseenSlangFailures(builder, validationFailures);
        AppendDebatableSection(
            builder,
            report.DebatableLabels
                .Where(label => string.Equals(label.DatasetSplit, "Validation", StringComparison.OrdinalIgnoreCase))
                .ToList());
    }

    private static void AppendDatasetDiagnostics(
        StringBuilder builder,
        BenchmarkReport report,
        string datasetSplit,
        string title)
    {
        var threads = report.ByThread
            .Where(thread => string.Equals(thread.DatasetSplit, datasetSplit, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (threads.Count == 0)
        {
            return;
        }

        var split = AggregateThreads(datasetSplit, threads);
        var failures = report.AllFailures
            .Where(failure => string.Equals(failure.DatasetSplit, datasetSplit, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var evaluations = report.TargetEvaluations
            .Where(evaluation => string.Equals(evaluation.DatasetSplit, datasetSplit, StringComparison.OrdinalIgnoreCase))
            .ToList();

        AppendConfusionMatrix(builder, $"{title} Fantasy Outlook Confusion Matrix", split.FantasyOutlookConfusionMatrix);
        AppendDatasetPlayerSummary(builder, $"{title} Player Summary", evaluations, failures);
        AppendDatasetFailureCategories(builder, $"{title} Failure Categories", failures);
    }

    private static void AppendDatasetPlayerSummary(
        StringBuilder builder,
        string title,
        IReadOnlyList<BenchmarkTargetEvaluation> evaluations,
        IReadOnlyList<BenchmarkFailure> failures)
    {
        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        builder.AppendLine("| Player | Scored mentions | Expected -/0/+ | Actual -/0/+ | Most common failure |");
        builder.AppendLine("| --- | ---: | --- | --- | --- |");

        foreach (var group in evaluations
                     .Where(IsScorableEvaluation)
                     .GroupBy(evaluation => evaluation.Player, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(group => group.Count())
                     .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                     .Take(12))
        {
            var expected = DirectionCounts(group.Select(evaluation => evaluation.ExpectedDirection));
            var actual = DirectionCounts(group.Select(evaluation => evaluation.ActualDirection));
            var commonFailure = failures
                .Where(failure => FailureMentionsPlayer(failure, group.Key))
                .GroupBy(failure => failure.Category, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(failureGroup => failureGroup.Count())
                .ThenBy(failureGroup => failureGroup.Key, StringComparer.OrdinalIgnoreCase)
                .Select(failureGroup => $"{failureGroup.Key} ({failureGroup.Count()})")
                .FirstOrDefault() ?? "none";

            builder.AppendLine(
                $"| {group.Key} | {group.Count()} | {expected.Negative}/{expected.Neutral}/{expected.Positive} | {actual.Negative}/{actual.Neutral}/{actual.Positive} | {commonFailure} |");
        }
    }

    private static void AppendDatasetFailureCategories(
        StringBuilder builder,
        string title,
        IReadOnlyList<BenchmarkFailure> failures)
    {
        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        foreach (var group in failures
                     .GroupBy(failure => failure.Category, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(group => group.Count())
                     .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"- {group.Key}: {group.Count()}");
        }
    }

    private static void AppendValidationPlayerSummary(
        StringBuilder builder,
        IReadOnlyList<BenchmarkTargetEvaluation> evaluations,
        IReadOnlyList<BenchmarkFailure> failures)
    {
        builder.AppendLine();
        builder.AppendLine("## Validation Player Summary");
        builder.AppendLine();
        builder.AppendLine("| Player | Scored mentions | Expected -/0/+ | Actual -/0/+ | Most common failure |");
        builder.AppendLine("| --- | ---: | --- | --- | --- |");

        foreach (var group in evaluations
                     .Where(IsScorableEvaluation)
                     .GroupBy(evaluation => evaluation.Player, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(group => group.Count())
                     .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                     .Take(12))
        {
            var expected = DirectionCounts(group.Select(evaluation => evaluation.ExpectedDirection));
            var actual = DirectionCounts(group.Select(evaluation => evaluation.ActualDirection));
            var commonFailure = failures
                .Where(failure => FailureMentionsPlayer(failure, group.Key))
                .GroupBy(CategorizeValidationFailure, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(failureGroup => failureGroup.Count())
                .ThenBy(failureGroup => failureGroup.Key, StringComparer.OrdinalIgnoreCase)
                .Select(failureGroup => $"{failureGroup.Key} ({failureGroup.Count()})")
                .FirstOrDefault() ?? "none";

            builder.AppendLine(
                $"| {group.Key} | {group.Count()} | {expected.Negative}/{expected.Neutral}/{expected.Positive} | {actual.Negative}/{actual.Neutral}/{actual.Positive} | {commonFailure} |");
        }
    }

    private static void AppendLondonTrajectory(
        StringBuilder builder,
        IReadOnlyList<BenchmarkTargetEvaluation> evaluations)
    {
        var london = evaluations
            .Where(evaluation => string.Equals(evaluation.Player, "Drake London", StringComparison.OrdinalIgnoreCase))
            .Where(IsScorableEvaluation)
            .OrderBy(evaluation => evaluation.CommentIndex)
            .ToList();
        if (london.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Drake London Validation Trajectory");
        builder.AppendLine();
        foreach (var (evaluation, index) in london.Select((evaluation, index) => (evaluation, index + 1)))
        {
            builder.AppendLine(
                $"- {index:00}. {evaluation.ExpectedDirection} -> model {evaluation.ActualDirection}: {Trim(evaluation.Body, 130)}");
        }
    }

    private static void AppendValidationFailureCategories(
        StringBuilder builder,
        IReadOnlyList<BenchmarkFailure> failures)
    {
        builder.AppendLine();
        builder.AppendLine("## Validation Failure Categories");
        builder.AppendLine();
        foreach (var group in failures
                     .GroupBy(CategorizeValidationFailure, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(group => group.Count())
                     .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"- {group.Key}: {group.Count()}");
        }
    }

    private static void AppendValidationRepresentativeFailures(
        StringBuilder builder,
        IReadOnlyList<BenchmarkFailure> failures)
    {
        builder.AppendLine();
        builder.AppendLine("## Representative Validation Failures");
        builder.AppendLine();
        foreach (var failure in failures.Take(25))
        {
            builder.AppendLine($"- [{CategorizeValidationFailure(failure)} / {failure.Category}] {failure.CommentId}");
            builder.AppendLine($"  - Text: {Trim(failure.Body, 180)}");
            builder.AppendLine($"  - Expected: {failure.Expected}");
            builder.AppendLine($"  - Actual: {failure.Actual}");
        }
    }

    private static void AppendUnseenSlangFailures(
        StringBuilder builder,
        IReadOnlyList<BenchmarkFailure> failures)
    {
        var unseen = failures
            .Where(failure => CategorizeValidationFailure(failure).Equals("UnseenSlang", StringComparison.OrdinalIgnoreCase))
            .ToList();
        builder.AppendLine();
        builder.AppendLine("## Unseen Slang Failures");
        builder.AppendLine();
        foreach (var failure in unseen)
        {
            builder.AppendLine($"- {failure.CommentId}: {Trim(failure.Body, 170)}");
            builder.AppendLine($"  - Expected: {failure.Expected}");
            builder.AppendLine($"  - Actual: {failure.Actual}");
        }
    }

    private static bool IsScorableEvaluation(BenchmarkTargetEvaluation evaluation)
    {
        return !string.Equals(evaluation.ContextRequirement, BenchmarkContextRequirements.GameEventContext, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(evaluation.ContextRequirement, BenchmarkContextRequirements.Ambiguous, StringComparison.OrdinalIgnoreCase)
               && (evaluation.ExpectedDirection.Equals(BenchmarkDirections.Negative, StringComparison.OrdinalIgnoreCase)
                   || evaluation.ExpectedDirection.Equals(BenchmarkDirections.Neutral, StringComparison.OrdinalIgnoreCase)
                   || evaluation.ExpectedDirection.Equals(BenchmarkDirections.Positive, StringComparison.OrdinalIgnoreCase));
    }

    private static (int Negative, int Neutral, int Positive) DirectionCounts(IEnumerable<string> values)
    {
        var list = values.ToList();
        return (
            list.Count(value => string.Equals(value, BenchmarkDirections.Negative, StringComparison.OrdinalIgnoreCase)),
            list.Count(value => string.Equals(value, BenchmarkDirections.Neutral, StringComparison.OrdinalIgnoreCase)),
            list.Count(value => string.Equals(value, BenchmarkDirections.Positive, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool FailureMentionsPlayer(BenchmarkFailure failure, string player)
    {
        return failure.Expected.StartsWith($"{player} ", StringComparison.OrdinalIgnoreCase)
               || failure.Actual.StartsWith($"{player} ", StringComparison.OrdinalIgnoreCase);
    }

    private static string CategorizeValidationFailure(BenchmarkFailure failure)
    {
        if (failure.Category.StartsWith("Target", StringComparison.OrdinalIgnoreCase))
        {
            return "TargetResolution";
        }

        if (string.Equals(failure.ContextRequirement, BenchmarkContextRequirements.GameEventContext, StringComparison.OrdinalIgnoreCase))
        {
            return "GameEventRequired";
        }

        if (string.Equals(failure.ContextRequirement, BenchmarkContextRequirements.ReplyContext, StringComparison.OrdinalIgnoreCase))
        {
            return "ReplyContext";
        }

        var body = failure.Body.ToLowerInvariant();
        if (body.Contains("built different")
            || body.Contains("dawg")
            || body.Contains("owners 9/11")
            || body.Contains("daddy")
            || body.Contains("stonks"))
        {
            return "UnseenSlang";
        }

        foreach (var category in new[]
                 {
                     "LineupRegret",
                     "DraftRegret",
                     "OpponentPerspective",
                     "OwnRosterPerspective",
                     "Comparison",
                     "Negation",
                     "Sarcasm",
                     "UsageOpportunity",
                     "QuarterbackPlay",
                     "Injury",
                     "Performance"
                 })
        {
            if (failure.Category.Contains(category, StringComparison.OrdinalIgnoreCase)
                || (failure.Notes?.Contains(category, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return category;
            }
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
