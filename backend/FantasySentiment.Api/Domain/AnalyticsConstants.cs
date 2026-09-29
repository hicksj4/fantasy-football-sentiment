namespace FantasySentiment.Api.Domain;

public static class SentimentCategories
{
    public const string Positive = "Positive";
    public const string Neutral = "Neutral";
    public const string Negative = "Negative";
}

public static class FantasyIntents
{
    public const string Add = "Add";
    public const string Drop = "Drop";
    public const string Start = "Start";
    public const string Sit = "Sit";
    public const string Buy = "Buy";
    public const string Sell = "Sell";
    public const string Hold = "Hold";
    public const string Injury = "Injury";
    public const string Hype = "Hype";
    public const string Panic = "Panic";
    public const string General = "General";

    public static readonly string[] All =
    [
        Add,
        Drop,
        Start,
        Sit,
        Buy,
        Sell,
        Hold,
        Injury,
        Hype,
        Panic,
        General
    ];
}

public static class FantasyActions
{
    public const string Add = FantasyIntents.Add;
    public const string Drop = FantasyIntents.Drop;
    public const string Start = FantasyIntents.Start;
    public const string Sit = FantasyIntents.Sit;
    public const string Buy = FantasyIntents.Buy;
    public const string Sell = FantasyIntents.Sell;
    public const string Hold = FantasyIntents.Hold;

    public static readonly string[] All =
    [
        Add,
        Drop,
        Start,
        Sit,
        Buy,
        Sell,
        Hold
    ];
}

public static class FantasyDiscussionSignals
{
    public const string Injury = FantasyIntents.Injury;

    public static readonly string[] All =
    [
        Injury
    ];
}
