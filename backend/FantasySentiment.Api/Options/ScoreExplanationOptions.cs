namespace FantasySentiment.Api.Options;

public sealed class ScoreExplanationOptions
{
    public int MostRejectedMaximumRedditScore { get; set; } = -3;
    public int CommentExcerptLength { get; set; } = 190;
}
