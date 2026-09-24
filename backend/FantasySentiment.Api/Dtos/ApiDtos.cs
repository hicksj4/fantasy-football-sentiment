namespace FantasySentiment.Api.Dtos;

public sealed record PlayerListItemDto(
    int Id,
    string Name,
    string Team,
    string Position,
    string[] Aliases);

public sealed record PlayerMetricsDto(
    int PlayerId,
    string Name,
    string Team,
    string Position,
    string[] Aliases,
    int Mentions,
    double Buzz,
    double Sentiment,
    double RawSentiment,
    double CommunitySentiment,
    int TotalRedditScore,
    double AverageVoteWeight,
    double SampleStrengthScore,
    string Confidence,
    double PositivePercent,
    double NeutralPercent,
    double NegativePercent,
    int AddCount,
    int DropCount,
    int StartCount,
    int SitCount,
    int BuyCount,
    int SellCount,
    int HoldCount,
    int InjuryCount,
    int HypeCount,
    int PanicCount);

public sealed record DashboardPlayerDto(
    int PlayerId,
    string Name,
    string Team,
    string Position,
    int Mentions,
    double Buzz,
    double Sentiment,
    double RawSentiment,
    double CommunitySentiment,
    double SentimentChange,
    int TotalRedditScore,
    double AverageVoteWeight,
    double SampleStrengthScore,
    string Confidence,
    int AddCount,
    int DropCount,
    int StartCount,
    int SitCount,
    int BuyCount,
    int SellCount,
    int HypeCount,
    int PanicCount);

public sealed record PlayerHistoryPointDto(
    DateTimeOffset Timestamp,
    int Mentions,
    double Sentiment,
    double RawSentiment,
    double CommunitySentiment,
    double SampleStrengthScore,
    string Confidence,
    double Buzz);

public sealed record TeamSummaryDto(
    string Team,
    int Mentions,
    double Sentiment,
    double Buzz,
    string? MostDiscussedPlayer,
    string? BiggestSentimentMover,
    string? BiggestBuzzMover);

public sealed record TeamDetailDto(
    string Team,
    int Mentions,
    double Sentiment,
    double Buzz,
    IReadOnlyList<DashboardPlayerDto> Players);
