namespace FantasySentiment.Api.Options;

public sealed class DashboardHighlightOptions
{
    public int MinimumMentions { get; set; } = 5;
    public double MinimumSampleStrength { get; set; } = 0.25;
    public bool ExcludeLowConfidence { get; set; } = true;
    public double MinimumAbsoluteSentimentChange { get; set; } = 0.05;
    public int CommentExcerptLength { get; set; } = 190;
}
