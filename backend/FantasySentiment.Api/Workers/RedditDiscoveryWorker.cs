using FantasySentiment.Api.Options;
using FantasySentiment.Api.Services;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Workers;

public sealed class RedditDiscoveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RedditAnalysisOptions> options,
    ILogger<RedditDiscoveryWorker> logger) : BackgroundService
{
    private readonly RedditAnalysisOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.DiscoveryIntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var ingestion = scope.ServiceProvider.GetRequiredService<RedditIngestionService>();
                await ingestion.DiscoverPostsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reddit discovery worker failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
