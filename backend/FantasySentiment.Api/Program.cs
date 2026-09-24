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
builder.Services.Configure<SentimentOptions>(builder.Configuration.GetSection("Sentiment"));
builder.Services.Configure<SentimentLexiconOptions>(builder.Configuration.GetSection("SentimentLexicon"));
builder.Services.Configure<IntentLexiconOptions>(builder.Configuration.GetSection("IntentLexicon"));

var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? BuildPostgresConnectionStringFromEnvironment();

builder.Services.AddDbContext<FantasySentimentDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddSingleton<PlayerMentionDetector>();
builder.Services.AddSingleton<MentionContextExtractor>();
builder.Services.AddSingleton<VoteWeightCalculator>();
builder.Services.AddSingleton<FantasySentimentAnalyzer>();
builder.Services.AddSingleton<RuleBasedFantasySentimentAnalyzer>();
builder.Services.AddSingleton<RobertaSentimentAnalyzer>();
builder.Services.AddSingleton<HybridSentimentAnalyzer>();
builder.Services.AddSingleton<ComparisonSentimentAnalyzer>();
builder.Services.AddSingleton<ISentimentAnalyzer>(provider =>
{
    var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SentimentOptions>>().Value;
    return options.Mode.Trim().ToLowerInvariant() switch
    {
        "rulebased" or "rule-based" => provider.GetRequiredService<RuleBasedFantasySentimentAnalyzer>(),
        "roberta" => provider.GetRequiredService<RobertaSentimentAnalyzer>(),
        "hybrid" => provider.GetRequiredService<HybridSentimentAnalyzer>(),
        "comparison" => provider.GetRequiredService<ComparisonSentimentAnalyzer>(),
        _ => provider.GetRequiredService<ComparisonSentimentAnalyzer>()
    };
});
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
app.Services.GetRequiredService<RobertaSentimentAnalyzer>()
    .EnsureUsableForMode(app.Configuration.GetValue("Sentiment:Mode", SentimentModes.RuleBased));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("frontend");

var api = app.MapGroup("/api");

api.MapGet("/health", (RobertaSentimentAnalyzer roberta) => Results.Ok(new
{
    status = "ok",
    timestamp = DateTimeOffset.UtcNow,
    mockReddit = app.Configuration.GetValue("Reddit:UseMockReddit", true),
    sentimentMode = app.Configuration.GetValue("Sentiment:Mode", SentimentModes.RuleBased),
    roberta = ToHealthStatus(roberta.Status, app.Environment.ContentRootPath)
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
    Override("SENTIMENT_MODE", "Sentiment:Mode");
    Override("SENTIMENT_ROBERTA_ENABLED", "Sentiment:Roberta:Enabled");
    Override("SENTIMENT_ROBERTA_MODEL_PATH", "Sentiment:Roberta:ModelPath");
    Override("SENTIMENT_ROBERTA_ONNX_MODEL_PATH", "Sentiment:Roberta:OnnxModelPath");
    Override("SENTIMENT_ROBERTA_TOKENIZER_PATH", "Sentiment:Roberta:TokenizerPath");
    Override("SENTIMENT_ROBERTA_CONFIG_PATH", "Sentiment:Roberta:ConfigPath");
    Override("SENTIMENT_ROBERTA_TOKENIZER_CONFIG_PATH", "Sentiment:Roberta:TokenizerConfigPath");
    Override("SENTIMENT_ROBERTA_MAX_SEQUENCE_LENGTH", "Sentiment:Roberta:MaxSequenceLength");

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

static object ToHealthStatus(RobertaRuntimeStatus status, string contentRootPath)
{
    return new
    {
        status.Enabled,
        status.Loaded,
        Model = new
        {
            Path = SafeRelativePath(contentRootPath, status.ModelPath),
            Exists = status.ModelExists
        },
        Tokenizer = new
        {
            Path = SafeRelativePath(contentRootPath, status.TokenizerPath),
            Exists = status.TokenizerExists
        },
        Config = new
        {
            Path = SafeRelativePath(contentRootPath, status.ConfigPath),
            Exists = status.ConfigExists
        },
        TokenizerConfig = new
        {
            Path = SafeRelativePath(contentRootPath, status.TokenizerConfigPath),
            Exists = status.TokenizerConfigExists
        },
        status.MaxSequenceLength,
        status.InputNames,
        status.OutputNames,
        status.ErrorMessage
    };
}

static string SafeRelativePath(string root, string path)
{
    try
    {
        return Path.GetRelativePath(root, path);
    }
    catch
    {
        return Path.GetFileName(path);
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
