namespace FantasySentiment.Api.Domain;

public static class AnalysisDispositions
{
    public const string Scored = "Scored";
    public const string MentionOnly = "MentionOnly";
}

public static class AnalysisDispositionReasons
{
    public const string Scored = "Scored";
    public const string LowSemanticConfidence = "LowSemanticConfidence";
    public const string WeakTargetAttribution = "WeakTargetAttribution";
}
