namespace FantasySentiment.Api.Domain;

public sealed class RedditComment
{
    public int Id { get; set; }
    public required string RedditId { get; set; }
    public int PostId { get; set; }
    public string? ParentRedditId { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Score { get; set; }

    public RedditPost Post { get; set; } = null!;
    public List<PlayerMention> PlayerMentions { get; set; } = [];
}
