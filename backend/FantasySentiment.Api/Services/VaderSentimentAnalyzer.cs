using System.Text.RegularExpressions;
using FantasySentiment.Api.Domain;
using VaderSharp2;

namespace FantasySentiment.Api.Services;

public sealed class VaderSentimentAnalyzer(
    FantasySentimentAnalyzer fantasySentimentAnalyzer) : ISentimentAnalyzer
{
    private readonly SentimentIntensityAnalyzer _vader = new();
    private readonly object _vaderLock = new();

    public ValueTask<TargetedSentimentResult> AnalyzeAsync(
        TargetedSentimentRequest request,
        CancellationToken cancellationToken)
    {
        var analysis = AnalyzeDetailed(request);
        var semanticRuleScore = Math.Round(
            analysis.FantasyAdjustment
            + analysis.EmojiAdjustment
            + analysis.StructuralAdjustment
            + analysis.PerspectiveAdjustment
            + analysis.QuestionAdjustment,
            3);

        return ValueTask.FromResult(new TargetedSentimentResult(
            analysis.RawVaderScore,
            analysis.FinalScore,
            TargetedSentimentResult.CategoryFromScore(analysis.FinalScore),
            semanticRuleScore,
            analysis.RawVaderScore,
            analysis.TotalAdjustment,
            nameof(VaderSentimentAnalyzer),
            request.Context,
            analysis.ToDiagnostics()));
    }

    public SentimentAnalysisResult AnalyzeDetailed(TargetedSentimentRequest request)
    {
        var targetText = ResolveTargetText(request);
        var preparation = fantasySentimentAnalyzer.PrepareForVader(targetText);
        var vader = ScoreVader(preparation.VaderText);
        var semantic = fantasySentimentAnalyzer.AnalyzeTarget(
            targetText,
            request.TargetPerspective,
            vader.Compound,
            preparation,
            request.Mention);
        semantic = ApplyReplyStance(request, semantic);

        return new SentimentAnalysisResult(
            targetText,
            request.TargetPerspective,
            preparation.VaderText,
            Math.Round(vader.Compound, 3),
            semantic.FantasyAdjustment,
            semantic.EmojiAdjustment,
            semantic.StructuralAdjustment,
            semantic.PerspectiveAdjustment,
            semantic.QuestionAdjustment,
            semantic.SemanticConfidence,
            semantic.FinalScore,
            semantic.MatchedPositiveTerms,
            semantic.MatchedNegativeTerms,
            semantic.MatchedPhrases,
            semantic.MatchedEmoji,
            semantic.AppliedRules);
    }

    private FantasySemanticAnalysis ApplyReplyStance(
        TargetedSentimentRequest request,
        FantasySemanticAnalysis semantic)
    {
        if (request.TargetAttributionMethod is not TargetAttributionMethods.ParentInherited
            and not TargetAttributionMethods.AncestorInherited)
        {
            return semantic;
        }

        var parent = ExtractContextValue(request.Context, "Parent");
        var comment = ExtractContextValue(request.Context, "Comment");
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(comment))
        {
            return semantic;
        }

        var stance = ClassifyReplyStance(comment);
        if (stance == ReplyStance.Neutral)
        {
            return semantic;
        }

        var parentPreparation = fantasySentimentAnalyzer.PrepareForVader(parent);
        var parentVader = ScoreVader(parentPreparation.VaderText);
        var parentSemantic = fantasySentimentAnalyzer.AnalyzeTarget(
            parent,
            request.TargetPerspective,
            parentVader.Compound,
            parentPreparation,
            request.Mention);
        if (Math.Abs(parentSemantic.FinalScore) < 0.15)
        {
            return semantic;
        }

        var inheritedScore = stance == ReplyStance.Agree
            ? parentSemantic.FinalScore * 0.85
            : parentSemantic.FinalScore <= -0.15
                ? 0.45
                : -0.45;
        inheritedScore = Math.Round(Math.Clamp(inheritedScore, -1, 1), 3);
        var rules = semantic.AppliedRules
            .Concat([
                stance == ReplyStance.Agree
                    ? $"replyAgreement={FormatSigned(inheritedScore)}"
                    : $"replyDisagreement={FormatSigned(inheritedScore)}"
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return semantic with
        {
            FinalScore = inheritedScore,
            SemanticConfidence = Math.Max(semantic.SemanticConfidence, 0.65),
            AppliedRules = rules
        };
    }

    private VaderScore ScoreVader(string context)
    {
        lock (_vaderLock)
        {
            var scores = _vader.PolarityScores(context);
            return new VaderScore(
                Math.Round(scores.Compound, 3),
                Math.Round(scores.Positive, 3),
                Math.Round(scores.Neutral, 3),
                Math.Round(scores.Negative, 3));
        }
    }

    private static string ResolveTargetText(TargetedSentimentRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.TargetText))
        {
            return request.TargetText;
        }

        var targetContext = request.Context
            .Split(Environment.NewLine, StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith("Target context:", StringComparison.OrdinalIgnoreCase));
        if (targetContext is not null)
        {
            return targetContext["Target context:".Length..].Trim();
        }

        return request.Context;
    }

    private static string? ExtractContextValue(string context, string label)
    {
        var prefix = $"{label}:";
        return context
            .Split(Environment.NewLine, StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ?[prefix.Length..]
            .Trim();
    }

    private static ReplyStance ClassifyReplyStance(string comment)
    {
        var normalized = NormalizeForReplyStance(comment);
        if (normalized.Length > 120)
        {
            return ReplyStance.Neutral;
        }

        if (Regex.IsMatch(
                normalized,
                @"^(?:nah|no|nope|disagree|not really|i disagree)\b|\b(?:he|him|they|that dude|this dude)\s+(?:will|ll|'ll|should|gonna|going to)\s+be\s+fine\b|\b(?:he'll|hell|they'll|theyll)\s+be\s+fine\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return ReplyStance.Disagree;
        }

        if (Regex.IsMatch(
                normalized,
                @"^(?:yeah|yes|yep|yup|agreed|agree|facts|exactly|true|same|this|for real|fr|absolutely|100)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return ReplyStance.Agree;
        }

        return ReplyStance.Neutral;
    }

    private static string NormalizeForReplyStance(string value)
    {
        var lowered = value.ToLowerInvariant();
        var normalized = Regex.Replace(lowered, @"[^a-z0-9']+", " ");
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private static string FormatSigned(double value)
    {
        return value >= 0 ? $"+{value:0.###}" : $"{value:0.###}";
    }

    private enum ReplyStance
    {
        Neutral,
        Agree,
        Disagree
    }

    private sealed record VaderScore(
        double Compound,
        double Positive,
        double Neutral,
        double Negative);
}
