namespace FantasySentiment.Api.Options;

public sealed class SentimentLexiconOptions
{
    public string[] PositiveTerms { get; set; } = [];
    public string[] NegativeTerms { get; set; } = [];
    public string[] PositivePhrases { get; set; } = [];
    public string[] NegativePhrases { get; set; } = [];
    public string[] PositiveSlang { get; set; } = [];
    public string[] NegativeSlang { get; set; } = [];
    public string[] Intensifiers { get; set; } = [];
    public string[] Negators { get; set; } = [];
    public SentimentEmojiLexiconOptions EmojiLexicon { get; set; } = new();
    public SentimentPerspectivePatternOptions PerspectivePatterns { get; set; } = new();
    public SentimentSarcasmOptions Sarcasm { get; set; } = new();
}

public sealed class SentimentEmojiLexiconOptions
{
    public Dictionary<string, double> Positive { get; set; } = new()
    {
        ["📈"] = 0.35,
        ["🔥"] = 0.30,
        ["🚀"] = 0.30,
        ["🐐"] = 0.40
    };

    public Dictionary<string, double> Negative { get; set; } = new()
    {
        ["📉"] = -0.35,
        ["💀"] = -0.25
    };
}

public sealed class SentimentPerspectivePatternOptions
{
    public string[] OpponentThreat { get; set; } =
    [
        "fantasy week already over",
        "week already over",
        "week is over",
        "ended my week",
        "killed me",
        "destroyed me",
        "wish i didn't play against",
        "wish i wasnt playing against",
        "wish i wasn't facing",
        "wish i didnt face"
    ];

    public string[] OwnRosterPositive { get; set; } =
    [
        "beyond happy",
        "so happy",
        "love",
        "incredible",
        "carried me"
    ];

    public string[] OwnRosterRegret { get; set; } =
    [
        "why did i start",
        "cost me my week",
        "ended my week",
        "lost me my week",
        "benched the wrong",
        "should have benched"
    ];
}

public sealed class SentimentSarcasmOptions
{
    public double LowPointMaximum { get; set; } = 5.0;
    public string[] LowPointPatterns { get; set; } =
    [
        "thanks for the {points} points",
        "thanks for {points} points",
        "love the {points} points",
        "great another {points} point game",
        "great, another {points} point game"
    ];
}
