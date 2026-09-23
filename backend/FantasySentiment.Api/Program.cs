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
builder.Services.Configure<SnapshotOptions>(builder.Configuration.GetSection("Snapshots"));
builder.Services.Configure<SentimentLexiconOptions>(builder.Configuration.GetSection("SentimentLexicon"));
builder.Services.Configure<IntentLexiconOptions>(builder.Configuration.GetSection("IntentLexicon"));

var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? BuildPostgresConnectionStringFromEnvironment();

builder.Services.AddDbContext<FantasySentimentDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddSingleton<PlayerMentionDetector>();
builder.Services.AddSingleton<FantasySentimentAnalyzer>();
builder.Services.AddSingleton<FantasyIntentClassifier>();
builder.Services.AddSingleton<BuzzScoreCalculator>();
builder.Services.AddScoped<SnapshotAggregationService>();
builder.Services.AddScoped<RedditIngestionService>();
builder.Services.AddScoped<AnalyticsQueryService>();
builder.Services.AddScoped<DemoDataSeeder>();

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
builder.Services.AddHostedService<RedditIngestionWorker>();
builder.Services.AddHostedService<SnapshotAggregationWorker>();

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
    mockReddit = app.Configuration.GetValue("Reddit:UseMockReddit", true)
}));

api.MapGet("/players", async (
    string? search,
    int? limit,
    AnalyticsQueryService analytics,
    CancellationToken cancellationToken) =>
{
    var players = await analytics.SearchPlayersAsync(search, limit ?? 30, cancellationToken);
    return Results.Ok(players);
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
