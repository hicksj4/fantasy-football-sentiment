namespace FantasySentiment.Api.Options;

public sealed class SentimentLexiconOptions
{
    public string[] PositiveTerms { get; set; } = [];
    public string[] NegativeTerms { get; set; } = [];
    public string[] Intensifiers { get; set; } = [];
    public string[] Negators { get; set; } = [];
}
