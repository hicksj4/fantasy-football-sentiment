namespace FantasySentiment.Api.Domain;

public sealed class RedditPost
{
    public int Id { get; set; }
    public required string RedditId { get; set; }
    public required string Subreddit { get; set; }
    public required string Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Url { get; set; }
    public int CommentCount { get; set; }

    public List<RedditComment> Comments { get; set; } = [];
}
