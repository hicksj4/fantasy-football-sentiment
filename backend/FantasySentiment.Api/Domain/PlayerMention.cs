namespace FantasySentiment.Api.Domain;

public sealed class PlayerMention
{
    public long Id { get; set; }
    public int? SnapshotId { get; set; }
    public int PlayerId { get; set; }
    public int CommentId { get; set; }
    public double RawSentimentScore { get; set; }
    public double FinalSentimentScore { get; set; }
    public double SentimentScore { get; set; }
    public double? ModelSentimentScore { get; set; }
    public double RuleBasedSentimentScore { get; set; }
    public double FantasyAdjustment { get; set; }
    public required string SentimentCategory { get; set; }
    public string SentimentAnalyzer { get; set; } = "Unknown";
    public string SentimentContext { get; set; } = string.Empty;
    public string? SentimentDiagnostics { get; set; }
    public string MatchedAlias { get; set; } = string.Empty;
    public string TargetAttributionMethod { get; set; } = TargetAttributionMethods.Explicit;
    public double TargetConfidence { get; set; } = 1;
    public required string Intent { get; set; }
    public int RedditScore { get; set; }
    public double VoteWeight { get; set; } = 1;
    public double WeightedSentimentContribution { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public AnalysisSnapshot? Snapshot { get; set; }
    public Player Player { get; set; } = null!;
    public RedditComment Comment { get; set; } = null!;
}
