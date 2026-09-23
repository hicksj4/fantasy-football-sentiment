using FantasySentiment.Api.Options;
using FantasySentiment.Api.Services;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Workers;

public sealed class RedditIngestionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RedditOptions> options,
    ILogger<RedditIngestionWorker> logger) : BackgroundService
{
    private readonly RedditOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(15, _options.IngestionIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var ingestion = scope.ServiceProvider.GetRequiredService<RedditIngestionService>();
                await ingestion.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reddit ingestion worker failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
