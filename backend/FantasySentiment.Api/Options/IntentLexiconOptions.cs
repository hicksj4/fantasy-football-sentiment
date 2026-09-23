namespace FantasySentiment.Api.Options;

public sealed class IntentLexiconOptions
{
    public string[] Add { get; set; } = [];
    public string[] Drop { get; set; } = [];
    public string[] Start { get; set; } = [];
    public string[] Sit { get; set; } = [];
    public string[] Buy { get; set; } = [];
    public string[] Sell { get; set; } = [];
    public string[] Hold { get; set; } = [];
    public string[] Injury { get; set; } = [];
    public string[] Hype { get; set; } = [];
    public string[] Panic { get; set; } = [];
}
