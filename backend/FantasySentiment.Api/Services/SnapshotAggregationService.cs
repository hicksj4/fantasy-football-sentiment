using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Services;

public sealed class SnapshotAggregationService(
    FantasySentimentDbContext db,
    BuzzScoreCalculator buzzScoreCalculator,
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

            var snapshot = existingSnapshots.TryGetValue(playerId, out var existing)
                ? existing
                : new PlayerSnapshot { PlayerId = playerId, Timestamp = timestamp };

            snapshot.MentionCount = recentMentions.Count;
            snapshot.PositiveCount = recentMentions.Count(mention => mention.SentimentCategory == SentimentCategories.Positive);
            snapshot.NegativeCount = recentMentions.Count(mention => mention.SentimentCategory == SentimentCategories.Negative);
            snapshot.NeutralCount = recentMentions.Count(mention => mention.SentimentCategory == SentimentCategories.Neutral);
            snapshot.RawSentimentScore = recentMentions.Count == 0
                ? 0
                : Math.Round(recentMentions.Average(mention => mention.RawSentimentScore), 3);
            snapshot.SentimentScore = recentMentions.Count == 0
                ? 0
                : Math.Round(recentMentions.Average(mention => mention.FinalSentimentScore), 3);
            snapshot.TotalRedditScore = recentMentions.Sum(mention => mention.RedditScore);
            snapshot.TotalVoteWeight = Math.Round(recentMentions.Sum(mention => mention.VoteWeight), 3);
            snapshot.AverageVoteWeight = recentMentions.Count == 0
                ? 0
                : Math.Round(recentMentions.Average(mention => mention.VoteWeight), 3);
            snapshot.CommunitySentimentScore = snapshot.TotalVoteWeight == 0
                ? 0
                : Math.Round(recentMentions.Sum(mention => mention.WeightedSentimentContribution) / snapshot.TotalVoteWeight, 3);
            var sampleConfidence = SampleConfidenceCalculator.Calculate(recentMentions.Count, snapshot.TotalVoteWeight);
            snapshot.SampleStrengthScore = sampleConfidence.SampleStrengthScore;
            snapshot.Confidence = sampleConfidence.Confidence;
            snapshot.AddCount = CountIntent(recentMentions, FantasyIntents.Add);
            snapshot.DropCount = CountIntent(recentMentions, FantasyIntents.Drop);
            snapshot.StartCount = CountIntent(recentMentions, FantasyIntents.Start);
            snapshot.SitCount = CountIntent(recentMentions, FantasyIntents.Sit);
            snapshot.BuyCount = CountIntent(recentMentions, FantasyIntents.Buy);
            snapshot.SellCount = CountIntent(recentMentions, FantasyIntents.Sell);
            snapshot.HoldCount = CountIntent(recentMentions, FantasyIntents.Hold);
            snapshot.InjuryCount = CountIntent(recentMentions, FantasyIntents.Injury);
            snapshot.HypeCount = CountIntent(recentMentions, FantasyIntents.Hype);
            snapshot.PanicCount = CountIntent(recentMentions, FantasyIntents.Panic);
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

    private static int CountIntent(IEnumerable<PlayerMention> mentions, string intent)
    {
        return mentions.Count(mention => mention.Intent == intent);
    }

    public static DateTimeOffset AlignTimestamp(DateTimeOffset timestamp, int intervalMinutes)
    {
        var intervalTicks = TimeSpan.FromMinutes(Math.Max(1, intervalMinutes)).Ticks;
        var alignedTicks = timestamp.UtcTicks - timestamp.UtcTicks % intervalTicks;
        return new DateTimeOffset(alignedTicks, TimeSpan.Zero);
    }
}
