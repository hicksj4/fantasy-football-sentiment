using System.Text.Json;
using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FantasySentiment.Api.Services;

public sealed class DemoDataSeeder(
    FantasySentimentDbContext db,
    IWebHostEnvironment environment,
    PlayerMentionDetector mentionDetector,
    ISentimentAnalyzer sentimentAnalyzer,
    MentionContextExtractor contextExtractor,
    FantasyIntentClassifier intentClassifier,
    VoteWeightCalculator voteWeightCalculator,
    SnapshotAggregationService snapshotAggregationService,
    ILogger<DemoDataSeeder> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await SeedPlayersAsync(cancellationToken);

        if (await db.RedditComments.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Skipping demo comments because the database already contains Reddit comments.");
            return;
        }

        await SeedHistoricalDiscussionsAsync(cancellationToken);
        await SeedHistoricalSnapshotsAsync(cancellationToken);
    }

    private async Task SeedPlayersAsync(CancellationToken cancellationToken)
    {
        if (await db.Players.AnyAsync(cancellationToken))
        {
            return;
        }

        var path = Path.Combine(environment.ContentRootPath, "SeedData", "players.sample.json");
        await using var stream = File.OpenRead(path);
        var players = await JsonSerializer.DeserializeAsync<List<PlayerSeedDto>>(stream, JsonOptions, cancellationToken)
                      ?? [];

        foreach (var seed in players)
        {
            db.Players.Add(new Player
            {
                Name = seed.Name,
                Team = seed.Team,
                Position = seed.Position,
                ExternalId = seed.ExternalId,
                IsActive = true,
                Aliases = seed.Aliases
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(alias => new PlayerAlias { Alias = alias })
                    .ToList()
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {PlayerCount} sample players.", players.Count);
    }

    private async Task SeedHistoricalDiscussionsAsync(CancellationToken cancellationToken)
    {
        var players = await db.Players
            .Include(player => player.Aliases)
            .Where(player => player.IsActive)
            .ToListAsync(cancellationToken);

        var random = new Random(20260922);
        var now = DateTimeOffset.UtcNow;
        var start = now.AddDays(-7);
        var postNumber = 0;
        var commentNumber = 0;

        for (var timestamp = start; timestamp <= now; timestamp = timestamp.AddMinutes(30))
        {
            var post = new RedditPost
            {
                RedditId = $"seed-post-{++postNumber}",
                Subreddit = "fantasyfootball",
                Title = "Daily fantasy football discussion",
                CreatedAt = timestamp,
                Url = $"https://reddit.com/r/fantasyfootball/comments/seed{postNumber}",
                CommentCount = 0
            };
            db.RedditPosts.Add(post);

            var bodies = BuildDemoComments(timestamp, now, random);
            post.CommentCount = bodies.Count;

            foreach (var body in bodies)
            {
                var comment = new RedditComment
                {
                    RedditId = $"seed-comment-{++commentNumber}",
                    Post = post,
                    ParentRedditId = null,
                    Body = body,
                    CreatedAt = timestamp.AddSeconds(random.Next(0, 600)),
                    Score = random.Next(-5, 80)
                };
                db.RedditComments.Add(comment);
                await db.SaveChangesAsync(cancellationToken);

                var voteWeight = voteWeightCalculator.Calculate(comment.Score);
                foreach (var match in mentionDetector.DetectMentions(body, players))
                {
                    var context = contextExtractor.ExtractContext(body, match);
                    var sentiment = await sentimentAnalyzer.AnalyzeAsync(
                        new TargetedSentimentRequest(body, context, match),
                        cancellationToken);

                    db.PlayerMentions.Add(new PlayerMention
                    {
                        PlayerId = match.PlayerId,
                        CommentId = comment.Id,
                        CreatedAt = comment.CreatedAt,
                        RawSentimentScore = sentiment.RawSentimentScore,
                        FinalSentimentScore = sentiment.FinalSentimentScore,
                        SentimentScore = sentiment.FinalSentimentScore,
                        ModelSentimentScore = sentiment.ModelScore,
                        RuleBasedSentimentScore = sentiment.RuleBasedScore,
                        FantasyAdjustment = sentiment.FantasyAdjustment,
                        SentimentCategory = sentiment.SentimentCategory,
                        SentimentAnalyzer = sentiment.AnalyzerName,
                        SentimentContext = Truncate(sentiment.Context, 1200) ?? string.Empty,
                        SentimentDiagnostics = Truncate(sentiment.Diagnostics, 1000),
                        MatchedAlias = Truncate(match.MatchedAlias, 160) ?? string.Empty,
                        Intent = intentClassifier.Classify(body, match),
                        RedditScore = comment.Score,
                        VoteWeight = voteWeight,
                        WeightedSentimentContribution = Math.Round(sentiment.FinalSentimentScore * voteWeight, 3)
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
            }
        }

        logger.LogInformation(
            "Seeded {CommentCount} historical mock comments and derived mentions.",
            commentNumber);
    }

    private async Task SeedHistoricalSnapshotsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var start = now.AddDays(-7);
        for (var timestamp = start; timestamp <= now; timestamp = timestamp.AddMinutes(30))
        {
            await snapshotAggregationService.GenerateSnapshotsAsync(timestamp, cancellationToken);
        }
    }

    private static List<string> BuildDemoComments(DateTimeOffset timestamp, DateTimeOffset now, Random random)
    {
        var hoursAgo = (now - timestamp).TotalHours;
        var comments = new List<string>();

        MaybeAdd(comments, random, 0.38, "CMC is still an automatic smash start. RB1 forever.");
        MaybeAdd(comments, random, 0.28, "Bijan is a monster. Buy high if you can.");
        MaybeAdd(comments, random, 0.24, "Breece Hall panic is overblown. I am holding.");
        MaybeAdd(comments, random, 0.22, "Trey McBride looks like a locked in TE1.");
        MaybeAdd(comments, random, 0.2, "Start Zay Flowers or Jaylen Waddle this week?");
        MaybeAdd(comments, random, 0.18, "Garrett Wilson usage looks elite even when the box score is quiet.");

        if (hoursAgo < 8)
        {
            MaybeAdd(comments, random, 0.82, "Puka looks absolutely elite today. League winner energy.");
            MaybeAdd(comments, random, 0.72, "Tank Bigsby is a must add if he is still on waivers.");
            MaybeAdd(comments, random, 0.65, "Should I drop Mark Andrews for Trey McBride? Andrews looks washed.");
            MaybeAdd(comments, random, 0.54, "Malik Nabers is cooking. Total breakout game.");
            MaybeAdd(comments, random, 0.5, "Kyle Pitts is unstartable. I am selling if anyone bites.");
            MaybeAdd(comments, random, 0.35, "Is De'Von Achane hurt or just limited? I am nervous.");
        }
        else if (hoursAgo < 24)
        {
            MaybeAdd(comments, random, 0.45, "Puka is starting to look like a weekly must start.");
            MaybeAdd(comments, random, 0.33, "Mark Andrews has me nervous but I am holding for now.");
            MaybeAdd(comments, random, 0.25, "Rome Odunze feels like a sneaky add before the hype hits.");
            MaybeAdd(comments, random, 0.24, "Drake London goose egg has me tilted. Bench until they prove it.");
            MaybeAdd(comments, random, 0.2, "Selling high on James Cook after this spike week.");
        }
        else
        {
            MaybeAdd(comments, random, 0.18, "Puka is fine but the targets have been quiet lately.");
            MaybeAdd(comments, random, 0.22, "Andrews should bounce back. Still a start for me.");
            MaybeAdd(comments, random, 0.18, "Kyle Pitts breakout is coming eventually, right?");
            MaybeAdd(comments, random, 0.15, "Jayden Reed is balling. Waiver steal if your league slept.");
        }

        if (comments.Count == 0)
        {
            comments.Add("General game day thread is quiet, but CMC still feels elite.");
        }

        return comments;
    }

    private static void MaybeAdd(List<string> comments, Random random, double probability, string body)
    {
        if (random.NextDouble() <= probability)
        {
            comments.Add(body);
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        return string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];
    }

    private sealed record PlayerSeedDto(
        string Name,
        string Team,
        string Position,
        string? ExternalId,
        string[] Aliases);
}
