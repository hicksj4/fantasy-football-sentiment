using FantasySentiment.Api.Options;
using FantasySentiment.Api.Services;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Workers;

public sealed class SnapshotAggregationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SnapshotOptions> options,
    ILogger<SnapshotAggregationWorker> logger) : BackgroundService
{
    private readonly SnapshotOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var snapshots = scope.ServiceProvider.GetRequiredService<SnapshotAggregationService>();
                await snapshots.GenerateSnapshotsAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Snapshot aggregation worker failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
