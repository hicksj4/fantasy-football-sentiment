namespace FantasySentiment.Api.Domain;

public sealed class PlayerSnapshot
{
    public int PlayerId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public int MentionCount { get; set; }
    public int ScoredMentionCount { get; set; }
    public int MentionOnlyCount { get; set; }
    public double SentimentCoverage { get; set; }
    public bool HasScoredSentiment { get; set; } = true;
    public int PositiveCount { get; set; }
    public int NegativeCount { get; set; }
    public int NeutralCount { get; set; }
    public double RawSentimentScore { get; set; }
    public double SentimentScore { get; set; }
    public double CommunitySentimentScore { get; set; }
    public int TotalRedditScore { get; set; }
    public double TotalVoteWeight { get; set; }
    public double AverageVoteWeight { get; set; }
    public double SampleStrengthScore { get; set; }
    public string Confidence { get; set; } = "Low";
    public int AddCount { get; set; }
    public int DropCount { get; set; }
    public int StartCount { get; set; }
    public int SitCount { get; set; }
    public int BuyCount { get; set; }
    public int SellCount { get; set; }
    public int HoldCount { get; set; }
    public int InjuryCount { get; set; }
    public int HypeCount { get; set; }
    public int PanicCount { get; set; }
    public double BuzzScore { get; set; }

    public Player Player { get; set; } = null!;
}
