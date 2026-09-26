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
    int PanicCount,
    double VotingEffect = 0,
    double DiscussionImpact = 0,
    int DiscussionImpactRank = 0,
    double DiscussionImpactPercentile = 0,
    double PositiveImpact = 0,
    double NegativeImpact = 0,
    double NetImpact = 0,
    int PositiveMentions = 0,
    int NeutralMentions = 0,
    int NegativeMentions = 0,
    PlayerScoreCommentDto? MostInfluentialComment = null,
    PlayerScoreCommentDto? MostEndorsedComment = null,
    PlayerScoreCommentDto? MostRejectedComment = null);

public sealed record AnalysisSnapshotDto(
    int Id,
    string Key,
    string Name,
    int Season,
    int Week,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    string Status,
    int TotalPosts,
    int TotalComments,
    int TotalPlayerMentions,
    int UniquePlayersMentioned,
    double ProcessingDurationMs,
    double CommentsPerSecond);

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
    int PanicCount,
    double DiscussionShare = 0,
    double PositiveImpact = 0,
    double NegativeImpact = 0,
    double NetImpact = 0,
    double ImpactScore = 0,
    double VotingEffect = 0,
    double DiscussionImpact = 0,
    int DiscussionImpactRank = 0,
    double DiscussionImpactPercentile = 0);

public sealed record DashboardHighlightsDto(
    DashboardHighlightPlayerDto? Positive,
    DashboardHighlightPlayerDto? Negative,
    DashboardHighlightEligibilityDto Eligibility);

public sealed record DashboardHighlightEligibilityDto(
    int MinimumMentions,
    double MinimumSampleStrength,
    bool ExcludeLowConfidence,
    double MinimumAbsoluteSentimentChange);

public sealed record DashboardHighlightPlayerDto(
    int PlayerId,
    string Name,
    string Team,
    string Position,
    string? HeadshotUrl,
    int Mentions,
    double Sentiment,
    double CommunitySentiment,
    double SentimentChange,
    double Impact,
    double TotalVoteWeight,
    double AverageVoteWeight,
    double SampleStrengthScore,
    string Confidence,
    DashboardHighlightInfluenceDto? TopInfluence);

public sealed record DashboardHighlightInfluenceDto(
    string CommentExcerpt,
    int RedditScore,
    double Sentiment,
    double VoteWeight,
    double SentimentContribution,
    string PostTitle,
    DateTimeOffset CreatedAt,
    string? Subreddit = null,
    string? TargetAttributionMethod = null,
    double? TargetConfidence = null);

public sealed record PlayerScoreCommentDto(
    string CommentExcerpt,
    string PostTitle,
    string Subreddit,
    int RedditScore,
    double VoteWeight,
    double FinalSentimentScore,
    double CommunityContribution,
    string TargetAttributionMethod,
    double TargetConfidence,
    DateTimeOffset CreatedAt);

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
