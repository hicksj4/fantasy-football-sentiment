namespace FantasySentiment.Api.Domain;

public sealed class RedditPost
{
    public int Id { get; set; }
    public required string RedditId { get; set; }
    public required string Subreddit { get; set; }
    public required string Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Url { get; set; }
    public int Score { get; set; }
    public int CommentCount { get; set; }
    public int PreviousCommentCount { get; set; }
    public DateTimeOffset LastObservedAt { get; set; }
    public DateTimeOffset? LastObservedCommentCountAt { get; set; }
    public DateTimeOffset? LastAnalyzedAt { get; set; }
    public int LastAnalyzedCommentCount { get; set; }
    public string AnalysisStatus { get; set; } = RedditAnalysisStatuses.Discovered;
    public string PostType { get; set; } = RedditPostTypes.Normal;
    public string? AnalysisStatusReason { get; set; }

    public List<RedditComment> Comments { get; set; } = [];
}
