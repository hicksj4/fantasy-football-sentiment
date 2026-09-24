namespace FantasySentiment.Api.Domain;

public sealed class AnalysisSnapshot
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public int Season { get; set; }
    public int Week { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string Status { get; set; } = AnalysisSnapshotStatuses.Pending;
    public int TotalPosts { get; set; }
    public int TotalComments { get; set; }
    public int TotalPlayerMentions { get; set; }
    public int UniquePlayersMentioned { get; set; }
    public double ProcessingDurationMs { get; set; }
    public double CommentsPerSecond { get; set; }

    public List<RedditPost> Posts { get; set; } = [];
    public List<SnapshotPlayerAggregate> PlayerAggregates { get; set; } = [];
}
