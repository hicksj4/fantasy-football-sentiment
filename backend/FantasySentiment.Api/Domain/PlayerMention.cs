namespace FantasySentiment.Api.Domain;

public sealed class PlayerMention
{
    public long Id { get; set; }
    public int PlayerId { get; set; }
    public int CommentId { get; set; }
    public double SentimentScore { get; set; }
    public required string SentimentCategory { get; set; }
    public required string Intent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Player Player { get; set; } = null!;
    public RedditComment Comment { get; set; } = null!;
}
