using FantasySentiment.Api.Data;
using FantasySentiment.Api.Options;
using FantasySentiment.Api.Providers;
using FantasySentiment.Api.Services;
using FantasySentiment.Api.Workers;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

LoadDotEnv(builder.Environment.ContentRootPath);
builder.Configuration.AddJsonFile("SeedData/sentiment-terms.json", optional: false, reloadOnChange: true);
ApplyEnvironmentOverrides(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:3000", "http://127.0.0.1:3000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.Configure<RedditOptions>(builder.Configuration.GetSection("Reddit"));
builder.Services.Configure<RedditAnalysisOptions>(builder.Configuration.GetSection("RedditAnalysis"));
builder.Services.Configure<SnapshotOptions>(builder.Configuration.GetSection("Snapshots"));
builder.Services.Configure<SnapshotProcessingOptions>(builder.Configuration.GetSection("SnapshotProcessing"));
builder.Services.Configure<DashboardHighlightOptions>(builder.Configuration.GetSection("DashboardHighlights"));
builder.Services.Configure<SentimentOptions>(builder.Configuration.GetSection("Sentiment"));
builder.Services.Configure<SentimentLexiconOptions>(builder.Configuration.GetSection("SentimentLexicon"));
builder.Services.Configure<IntentLexiconOptions>(builder.Configuration.GetSection("IntentLexicon"));

var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? BuildPostgresConnectionStringFromEnvironment();

builder.Services.AddDbContext<FantasySentimentDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddSingleton<PlayerMentionDetector>();
builder.Services.AddSingleton<ICommentTargetResolver, CommentTargetResolver>();
builder.Services.AddSingleton<MentionContextExtractor>();
builder.Services.AddSingleton<TargetContextBuilder>();
builder.Services.AddSingleton<VoteWeightCalculator>();
builder.Services.AddSingleton<FantasySentimentAnalyzer>();
builder.Services.AddSingleton<VaderSentimentAnalyzer>();
builder.Services.AddSingleton<RuleBasedFantasySentimentAnalyzer>();
builder.Services.AddSingleton<ISentimentAnalyzer>(provider =>
{
    var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SentimentOptions>>().Value;
    return options.Mode.Trim().ToLowerInvariant() switch
    {
        "rulebased" or "rule-based" => provider.GetRequiredService<RuleBasedFantasySentimentAnalyzer>(),
        "vader" => provider.GetRequiredService<VaderSentimentAnalyzer>(),
        _ => provider.GetRequiredService<VaderSentimentAnalyzer>()
    };
});
builder.Services.AddSingleton<FantasyIntentClassifier>();
builder.Services.AddSingleton<BuzzScoreCalculator>();
builder.Services.AddSingleton<RedditPostAnalysisDecider>();
builder.Services.AddSingleton<ThreadContextBuilder>();
builder.Services.AddScoped<SnapshotAggregationService>();
builder.Services.AddScoped<RedditIngestionService>();
builder.Services.AddScoped<AnalyticsQueryService>();
builder.Services.AddScoped<SnapshotProcessingService>();
builder.Services.AddScoped<SnapshotQueryService>();
builder.Services.AddScoped<DemoDataSeeder>();
builder.Services.AddSingleton<ISnapshotSource, JsonSnapshotSource>();

builder.Services.AddSingleton<MockRedditDataProvider>();
builder.Services.AddHttpClient<RedditApiDataProvider>();
builder.Services.AddScoped<IRedditDataProvider>(provider =>
{
    var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedditOptions>>().Value;
    return options.UseMockReddit
        ? provider.GetRequiredService<MockRedditDataProvider>()
        : provider.GetRequiredService<RedditApiDataProvider>();
});

builder.Services.AddHostedService<StartupHostedService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("frontend");

var api = app.MapGroup("/api");

api.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    timestamp = DateTimeOffset.UtcNow,
    mockReddit = app.Configuration.GetValue("Reddit:UseMockReddit", true),
    snapshotMode = true,
    defaultSnapshotKey = app.Configuration.GetValue("SnapshotProcessing:DefaultSnapshotKey", "2026-week-4"),
    sentimentMode = ResolveSentimentModeName(app.Configuration.GetValue("Sentiment:Mode", SentimentModes.Vader))
}));

if (app.Environment.IsDevelopment())
{
    api.MapGet("/debug/reddit-posts", async (
        FantasySentimentDbContext db,
        CancellationToken cancellationToken) =>
    {
        var posts = await db.RedditPosts
            .AsNoTracking()
            .OrderByDescending(post => post.LastObservedAt)
            .Take(50)
            .Select(post => new
            {
                post.RedditId,
                post.Title,
                post.PostType,
                post.CommentCount,
                post.Score,
                post.AnalysisStatus,
                post.AnalysisStatusReason,
                post.LastObservedAt,
                post.LastAnalyzedAt,
                post.LastAnalyzedCommentCount
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(posts.Select(post => new
        {
            post.RedditId,
            post.Title,
            post.PostType,
            post.CommentCount,
            post.Score,
            post.AnalysisStatus,
            post.AnalysisStatusReason,
            post.LastObservedAt,
            post.LastAnalyzedAt,
            NewCommentsSinceLastAnalysis = Math.Max(0, post.CommentCount - post.LastAnalyzedCommentCount)
        }));
    });
}

api.MapGet("/players", async (
    string? search,
    int? limit,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var players = await analytics.SearchPlayersAsync(search, limit ?? 30, cancellationToken);
    return Results.Ok(players);
});

api.MapGet("/snapshots", async (
    SnapshotQueryService snapshots,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await snapshots.GetSnapshotsAsync(cancellationToken));
});

api.MapGet("/snapshots/{key}", async (
    string key,
    SnapshotQueryService snapshots,
    CancellationToken cancellationToken) =>
{
    var snapshot = await snapshots.GetSnapshotAsync(key, cancellationToken);
    return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
});

api.MapPost("/snapshots/{key}/process", async (
    string key,
    bool? skipIfProcessed,
    SnapshotProcessingService processor,
    SnapshotQueryService snapshots,
    CancellationToken cancellationToken) =>
{
    var processed = await processor.ProcessAsync(
        key,
        skipIfProcessed: skipIfProcessed ?? false,
        cancellationToken);
    var snapshot = await snapshots.GetSnapshotAsync(processed.Key, cancellationToken);
    return Results.Ok(snapshot);
});

api.MapGet("/snapshots/{key}/players", async (
    string key,
    string? sort,
    int? limit,
    SnapshotQueryService snapshots,
    CancellationToken cancellationToken) =>
{
    var rows = await snapshots.GetPlayersAsync(key, cancellationToken);
    var ordered = (sort?.Trim().ToLowerInvariant()) switch
    {
        "waivers" or "waiver" => rows
            .OrderByDescending(row => row.AddCount + row.HypeCount)
            .ThenByDescending(row => row.ImpactScore),
        "panic" => rows
            .OrderByDescending(row => row.DropCount + row.SitCount + row.SellCount + row.PanicCount)
            .ThenByDescending(row => row.ImpactScore),
        "most-discussed" or "mentions" => rows
            .OrderByDescending(row => row.Mentions)
            .ThenByDescending(row => row.ImpactScore),
        "positive" => rows
            .OrderByDescending(row => row.PositiveImpact)
            .ThenByDescending(row => row.Mentions),
        "negative" => rows
            .OrderBy(row => row.NegativeImpact)
            .ThenByDescending(row => row.Mentions),
        _ => rows
            .OrderByDescending(row => row.ImpactScore)
            .ThenByDescending(row => row.Mentions)
    };

    return Results.Ok(ordered.Take(limit ?? 20));
});

api.MapGet("/snapshots/{key}/players/{id:int}", async (
    string key,
    int id,
    SnapshotQueryService snapshots,
    CancellationToken cancellationToken) =>
{
    var player = await snapshots.GetPlayerAsync(key, id, cancellationToken);
    return player is null ? Results.NotFound() : Results.Ok(player);
});

api.MapGet("/snapshots/{key}/highlights", async (
    string key,
    SnapshotQueryService snapshots,
    CancellationToken cancellationToken) =>
{
    var highlights = await snapshots.GetHighlightsAsync(key, cancellationToken);
    return highlights is null ? Results.NotFound() : Results.Ok(highlights);
});

api.MapGet("/snapshots/{key}/teams", async (
    string key,
    SnapshotQueryService snapshots,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await snapshots.GetTeamSummariesAsync(key, cancellationToken));
});

api.MapGet("/snapshots/{key}/teams/{team}", async (
    string key,
    string team,
    SnapshotQueryService snapshots,
    CancellationToken cancellationToken) =>
{
    var detail = await snapshots.GetTeamDetailAsync(key, team, cancellationToken);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
});

api.MapGet("/search", async (
    string q,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var players = await analytics.SearchPlayersAsync(q, 10, cancellationToken);
    return Results.Ok(players);
});

api.MapGet("/players/{id:int}", async (
    int id,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var player = await analytics.GetPlayerAsync(id, cancellationToken);
    return player is null ? Results.NotFound() : Results.Ok(player);
});

api.MapGet("/players/{id:int}/history", async (
    int id,
    string? window,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var history = await analytics.GetPlayerHistoryAsync(id, ParseWindow(window), cancellationToken);
    return Results.Ok(history);
});

api.MapGet("/players/{id:int}/sentiment", async (
    int id,
    string? window,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var history = await analytics.GetPlayerHistoryAsync(id, ParseWindow(window), cancellationToken);
    return Results.Ok(new
    {
        playerId = id,
        latest = history.LastOrDefault(),
        history
    });
});

api.MapGet("/trending", async (
    string? window,
    int? limit,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var rows = await analytics.GetDashboardRowsAsync(ParseWindow(window), cancellationToken);
    return Results.Ok(rows
        .Where(row => row.Mentions > 0)
        .OrderByDescending(row => row.Buzz)
        .ThenByDescending(row => row.Mentions)
        .Take(limit ?? 12));
});

api.MapGet("/buzz", async (
    string? window,
    int? limit,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var rows = await analytics.GetDashboardRowsAsync(ParseWindow(window), cancellationToken);
    return Results.Ok(rows
        .Where(row => row.Mentions > 0)
        .OrderByDescending(row => row.Buzz)
        .Take(limit ?? 20));
});

api.MapGet("/highlights", async (
    string? window,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var highlights = await analytics.GetHighlightsAsync(ParseWindow(window), cancellationToken);
    return Results.Ok(highlights);
});

api.MapGet("/sentiment/risers", async (
    string? window,
    int? limit,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var rows = await analytics.GetDashboardRowsAsync(ParseWindow(window), cancellationToken);
    return Results.Ok(rows
        .Where(row => row.Mentions >= 2)
        .OrderByDescending(row => row.SentimentChange)
        .ThenByDescending(row => row.Mentions)
        .Take(limit ?? 12));
});

api.MapGet("/sentiment/fallers", async (
    string? window,
    int? limit,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var rows = await analytics.GetDashboardRowsAsync(ParseWindow(window), cancellationToken);
    return Results.Ok(rows
        .Where(row => row.Mentions >= 2)
        .OrderBy(row => row.SentimentChange)
        .ThenByDescending(row => row.Mentions)
        .Take(limit ?? 12));
});

api.MapGet("/waivers", async (
    string? window,
    int? limit,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var rows = await analytics.GetDashboardRowsAsync(ParseWindow(window), cancellationToken);
    return Results.Ok(rows
        .Where(row => row.Mentions > 0)
        .OrderByDescending(row => row.AddCount + row.HypeCount)
        .ThenByDescending(row => row.Buzz)
        .Take(limit ?? 12));
});

api.MapGet("/panic", async (
    string? window,
    int? limit,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var rows = await analytics.GetDashboardRowsAsync(ParseWindow(window), cancellationToken);
    return Results.Ok(rows
        .Where(row => row.Mentions > 0)
        .OrderByDescending(row => row.DropCount + row.SitCount + row.SellCount + row.PanicCount)
        .ThenByDescending(row => row.Buzz)
        .Take(limit ?? 12));
});

api.MapGet("/most-discussed", async (
    string? window,
    int? limit,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var rows = await analytics.GetDashboardRowsAsync(ParseWindow(window), cancellationToken);
    return Results.Ok(rows
        .Where(row => row.Mentions > 0)
        .OrderByDescending(row => row.Mentions)
        .Take(limit ?? 20));
});

api.MapGet("/teams", async (
    string? window,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var teams = await analytics.GetTeamSummariesAsync(ParseWindow(window), cancellationToken);
    return Results.Ok(teams);
});

api.MapGet("/teams/{team}", async (
    string team,
    string? window,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var detail = await analytics.GetTeamDetailAsync(team, ParseWindow(window), cancellationToken);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
});

app.Run();

static TimeSpan ParseWindow(string? window)
{
    return window?.Trim().ToLowerInvariant() switch
    {
        "1h" or "1 hour" => TimeSpan.FromHours(1),
        "6h" or "6 hours" => TimeSpan.FromHours(6),
        "7d" or "7 days" => TimeSpan.FromDays(7),
        "24h" or "24 hours" or null or "" => TimeSpan.FromHours(24),
        _ => TimeSpan.FromHours(24)
    };
}

static void ApplyEnvironmentOverrides(ConfigurationManager configuration)
{
    var explicitConnectionString = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrWhiteSpace(explicitConnectionString))
    {
        configuration["ConnectionStrings:Default"] = explicitConnectionString;
    }
    else if (HasAnyFantasyPostgresEnvironmentSetting()
             && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__Default")))
    {
        configuration["ConnectionStrings:Default"] = BuildPostgresConnectionStringFromEnvironment();
    }

    Override("USE_MOCK_REDDIT", "Reddit:UseMockReddit");
    Override("REDDIT_CLIENT_ID", "Reddit:ClientId");
    Override("REDDIT_CLIENT_SECRET", "Reddit:ClientSecret");
    Override("REDDIT_USER_AGENT", "Reddit:UserAgent");
    Override("REDDIT_FETCH_LOOKBACK_MINUTES", "Reddit:FetchLookbackMinutes");
    Override("SENTIMENT_MODE", "Sentiment:Mode");
    Override("REDDIT_ANALYSIS_DISCOVERY_INTERVAL_MINUTES", "RedditAnalysis:DiscoveryIntervalMinutes");
    Override("REDDIT_ANALYSIS_ANALYSIS_INTERVAL_MINUTES", "RedditAnalysis:AnalysisIntervalMinutes");
    Override("REDDIT_ANALYSIS_NORMAL_MINIMUM_COMMENTS", "RedditAnalysis:NormalPosts:MinimumComments");
    Override("REDDIT_ANALYSIS_NORMAL_MINIMUM_COMMENTS_WITH_SCORE", "RedditAnalysis:NormalPosts:MinimumCommentsWithScore");
    Override("REDDIT_ANALYSIS_NORMAL_MINIMUM_POST_SCORE", "RedditAnalysis:NormalPosts:MinimumPostScore");
    Override("REDDIT_ANALYSIS_IMPORTANT_NEWS_MINIMUM_COMMENTS", "RedditAnalysis:ImportantNews:MinimumComments");
    Override("REDDIT_ANALYSIS_GAME_MINIMUM_COMMENTS", "RedditAnalysis:GameThreads:MinimumComments");
    Override("REDDIT_ANALYSIS_GAME_MINIMUM_AGE_MINUTES", "RedditAnalysis:GameThreads:MinimumAgeMinutes");
    Override("REDDIT_ANALYSIS_GAME_COOLDOWN_WINDOW_MINUTES", "RedditAnalysis:GameThreads:CooldownWindowMinutes");
    Override("REDDIT_ANALYSIS_GAME_MAX_NEW_COMMENTS_DURING_COOLDOWN", "RedditAnalysis:GameThreads:MaximumNewCommentsDuringCooldown");
    Override("SNAPSHOT_DEFAULT_KEY", "SnapshotProcessing:DefaultSnapshotKey");
    Override("SNAPSHOT_PROCESS_ON_STARTUP", "SnapshotProcessing:ProcessMockSnapshotOnStartup");
    Override("SNAPSHOT_FIXTURE_DIRECTORY", "SnapshotProcessing:FixtureDirectory");
    Override("DASHBOARD_HIGHLIGHT_MINIMUM_MENTIONS", "DashboardHighlights:MinimumMentions");
    Override("DASHBOARD_HIGHLIGHT_MINIMUM_SAMPLE_STRENGTH", "DashboardHighlights:MinimumSampleStrength");
    Override("DASHBOARD_HIGHLIGHT_EXCLUDE_LOW_CONFIDENCE", "DashboardHighlights:ExcludeLowConfidence");
    Override("DASHBOARD_HIGHLIGHT_MINIMUM_ABSOLUTE_SENTIMENT_CHANGE", "DashboardHighlights:MinimumAbsoluteSentimentChange");
    Override("DASHBOARD_HIGHLIGHT_COMMENT_EXCERPT_LENGTH", "DashboardHighlights:CommentExcerptLength");

    void Override(string envName, string configKey)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[configKey] = value;
        }
    }

    static bool HasAnyFantasyPostgresEnvironmentSetting()
    {
        return new[]
        {
            "FANTASY_POSTGRES_HOST",
            "FANTASY_POSTGRES_PORT",
            "FANTASY_POSTGRES_DB",
            "FANTASY_POSTGRES_USER",
            "FANTASY_POSTGRES_PASSWORD"
        }.Any(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)));
    }

}

static string ResolveSentimentModeName(string? configuredMode)
{
    return configuredMode?.Trim().ToLowerInvariant() switch
    {
        "rulebased" or "rule-based" => SentimentModes.RuleBased,
        "vader" => SentimentModes.Vader,
        _ => SentimentModes.Vader
    };
}

static string BuildPostgresConnectionStringFromEnvironment()
{
    var portValue = Environment.GetEnvironmentVariable("FANTASY_POSTGRES_PORT");
    var port = int.TryParse(portValue, out var parsedPort) ? parsedPort : 55432;

    return new NpgsqlConnectionStringBuilder
    {
        Host = Environment.GetEnvironmentVariable("FANTASY_POSTGRES_HOST") ?? "localhost",
        Port = port,
        Database = Environment.GetEnvironmentVariable("FANTASY_POSTGRES_DB") ?? "fantasy_sentiment",
        Username = Environment.GetEnvironmentVariable("FANTASY_POSTGRES_USER") ?? "fantasy",
        Password = Environment.GetEnvironmentVariable("FANTASY_POSTGRES_PASSWORD") ?? "replace-with-local-password"
    }.ConnectionString;
}

static void LoadDotEnv(string contentRootPath)
{
    var candidates = new[]
    {
        Path.Combine(contentRootPath, ".env"),
        Path.GetFullPath(Path.Combine(contentRootPath, "..", "..", ".env"))
    };

    var path = candidates.FirstOrDefault(File.Exists);
    if (path is null)
    {
        return;
    }

    foreach (var rawLine in File.ReadLines(path))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        var equalsIndex = line.IndexOf('=');
        if (equalsIndex <= 0)
        {
            continue;
        }

        var key = line[..equalsIndex].Trim();
        var value = line[(equalsIndex + 1)..].Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(key) && Environment.GetEnvironmentVariable(key) is null)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
