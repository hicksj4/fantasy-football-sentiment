using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Services;

public sealed class SnapshotAggregationService(
    FantasySentimentDbContext db,
    BuzzScoreCalculator buzzScoreCalculator,
    SentimentScoringPolicy sentimentScoringPolicy,
    IOptions<SnapshotOptions> options,
    ILogger<SnapshotAggregationService> logger)
{
    private readonly SnapshotOptions _options = options.Value;

    public async Task<int> GenerateSnapshotsAsync(DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        timestamp = AlignTimestamp(timestamp, _options.IntervalMinutes);
        var recentWindow = TimeSpan.FromMinutes(_options.RecentWindowMinutes);
        var baselineWindow = TimeSpan.FromHours(_options.BaselineWindowHours);
        var recentStart = timestamp - recentWindow;
        var baselineStart = timestamp - baselineWindow - recentWindow;

        var players = await db.Players
            .AsNoTracking()
            .Where(player => player.IsActive)
            .Select(player => player.Id)
            .ToListAsync(cancellationToken);

        var mentions = await db.PlayerMentions
            .AsNoTracking()
            .Where(mention => mention.CreatedAt >= baselineStart && mention.CreatedAt <= timestamp)
            .ToListAsync(cancellationToken);

        var existingSnapshots = await db.PlayerSnapshots
            .Where(snapshot => snapshot.Timestamp == timestamp)
            .ToDictionaryAsync(snapshot => snapshot.PlayerId, cancellationToken);

        foreach (var playerId in players)
        {
            var recentMentions = mentions
                .Where(mention => mention.PlayerId == playerId
                                  && mention.CreatedAt >= recentStart
                                  && mention.CreatedAt <= timestamp)
                .ToList();

            var baselineMentions = mentions.Count(mention =>
                mention.PlayerId == playerId
                && mention.CreatedAt >= baselineStart
                && mention.CreatedAt < recentStart);
            var scoredMentions = recentMentions
                .Where(sentimentScoringPolicy.CanScore)
                .ToList();
            var hasScoredSentiment = scoredMentions.Count > 0;

            var snapshot = existingSnapshots.TryGetValue(playerId, out var existing)
                ? existing
                : new PlayerSnapshot { PlayerId = playerId, Timestamp = timestamp };

            snapshot.MentionCount = recentMentions.Count;
            snapshot.ScoredMentionCount = scoredMentions.Count;
            snapshot.MentionOnlyCount = recentMentions.Count - scoredMentions.Count;
            snapshot.SentimentCoverage = recentMentions.Count == 0
                ? 0
                : Math.Round(scoredMentions.Count / (double)recentMentions.Count, 3);
            snapshot.HasScoredSentiment = hasScoredSentiment;
            snapshot.PositiveCount = scoredMentions.Count(mention => mention.SentimentCategory == SentimentCategories.Positive);
            snapshot.NegativeCount = scoredMentions.Count(mention => mention.SentimentCategory == SentimentCategories.Negative);
            snapshot.NeutralCount = scoredMentions.Count(mention => mention.SentimentCategory == SentimentCategories.Neutral);
            snapshot.RawSentimentScore = hasScoredSentiment
                ? Math.Round(scoredMentions.Average(mention => mention.RawSentimentScore), 3)
                : 0;
            snapshot.SentimentScore = hasScoredSentiment
                ? Math.Round(scoredMentions.Average(mention => mention.FinalSentimentScore), 3)
                : 0;
            snapshot.TotalRedditScore = recentMentions.Sum(mention => mention.RedditScore);
            snapshot.TotalVoteWeight = Math.Round(scoredMentions.Sum(mention => mention.VoteWeight), 3);
            snapshot.AverageVoteWeight = hasScoredSentiment
                ? Math.Round(scoredMentions.Average(mention => mention.VoteWeight), 3)
                : 0;
            snapshot.CommunitySentimentScore = snapshot.TotalVoteWeight == 0
                ? 0
                : Math.Round(scoredMentions.Sum(mention => mention.WeightedSentimentContribution) / snapshot.TotalVoteWeight, 3);
            var sampleConfidence = SampleConfidenceCalculator.Calculate(scoredMentions.Count, snapshot.TotalVoteWeight);
            snapshot.SampleStrengthScore = sampleConfidence.SampleStrengthScore;
            snapshot.Confidence = sampleConfidence.Confidence;
            snapshot.AddCount = CountAction(recentMentions, FantasyActions.Add);
            snapshot.DropCount = CountAction(recentMentions, FantasyActions.Drop);
            snapshot.StartCount = CountAction(recentMentions, FantasyActions.Start);
            snapshot.SitCount = CountAction(recentMentions, FantasyActions.Sit);
            snapshot.BuyCount = CountAction(recentMentions, FantasyActions.Buy);
            snapshot.SellCount = CountAction(recentMentions, FantasyActions.Sell);
            snapshot.HoldCount = CountAction(recentMentions, FantasyActions.Hold);
            snapshot.InjuryCount = CountSignal(recentMentions, FantasyDiscussionSignals.Injury);
            snapshot.HypeCount = 0;
            snapshot.PanicCount = 0;
            snapshot.BuzzScore = buzzScoreCalculator.Calculate(
                recentMentions.Count,
                recentWindow,
                baselineMentions,
                baselineWindow);

            if (!existingSnapshots.ContainsKey(playerId))
            {
                db.PlayerSnapshots.Add(snapshot);
            }
        }

        var written = await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Generated player snapshots at {Timestamp}. Rows changed: {RowsChanged}.", timestamp, written);
        return players.Count;
    }

    private static int CountAction(IEnumerable<PlayerMention> mentions, string action)
    {
        return mentions.Count(mention => HasLabel(mention.FantasyActions, action));
    }

    private static int CountSignal(IEnumerable<PlayerMention> mentions, string signal)
    {
        return mentions.Count(mention => HasLabel(mention.DiscussionSignals, signal));
    }

    private static bool HasLabel(string labels, string expected)
    {
        return labels
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(expected, StringComparer.OrdinalIgnoreCase);
    }

    public static DateTimeOffset AlignTimestamp(DateTimeOffset timestamp, int intervalMinutes)
    {
        var intervalTicks = TimeSpan.FromMinutes(Math.Max(1, intervalMinutes)).Ticks;
        var alignedTicks = timestamp.UtcTicks - timestamp.UtcTicks % intervalTicks;
        return new DateTimeOffset(alignedTicks, TimeSpan.Zero);
    }
}
