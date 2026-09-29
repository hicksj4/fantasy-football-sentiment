using FantasySentiment.Api.Domain;

namespace FantasySentiment.Api.Services;

public sealed class SentimentScoringPolicy
{
    public const double MinimumSemanticConfidence = 0.45;
    public const double StrictDiagnosticSemanticConfidence = 0.75;
    public const double MinimumTargetConfidence = 0.50;

    public SentimentScoringDecision Evaluate(
        double semanticConfidence,
        double targetConfidence)
    {
        if (targetConfidence < MinimumTargetConfidence)
        {
            return new SentimentScoringDecision(
                AnalysisDispositions.MentionOnly,
                AnalysisDispositionReasons.WeakTargetAttribution);
        }

        if (semanticConfidence < MinimumSemanticConfidence)
        {
            return new SentimentScoringDecision(
                AnalysisDispositions.MentionOnly,
                AnalysisDispositionReasons.LowSemanticConfidence);
        }

        return new SentimentScoringDecision(
            AnalysisDispositions.Scored,
            AnalysisDispositionReasons.Scored);
    }

    public bool CanScore(PlayerMention mention)
    {
        return IsScoredDisposition(mention.AnalysisDisposition);
    }

    public static bool IsScoredDisposition(string? disposition)
    {
        return string.Equals(disposition, AnalysisDispositions.Scored, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record SentimentScoringDecision(
    string Disposition,
    string Reason)
{
    public bool IsScored => SentimentScoringPolicy.IsScoredDisposition(Disposition);
}
