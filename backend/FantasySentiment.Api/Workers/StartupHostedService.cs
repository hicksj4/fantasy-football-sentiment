using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using FantasySentiment.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Workers;

public sealed class StartupHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<SnapshotProcessingOptions> snapshotOptions,
    ILogger<StartupHostedService> logger) : IHostedService
{
    private readonly SnapshotProcessingOptions _snapshotOptions = snapshotOptions.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FantasySentimentDbContext>();

        var migrationsAssembly = db.GetService<IMigrationsAssembly>();
        if (migrationsAssembly.Migrations.Count > 0)
        {
            logger.LogInformation("Applying EF Core migrations.");
            await EnsureMigrationsHistoryTableExistsAsync(db, cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            logger.LogWarning("No EF Core migrations found. Falling back to EnsureCreated for local MVP startup.");
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }

        var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
        await seeder.SeedAsync(cancellationToken);

        if (_snapshotOptions.ProcessMockSnapshotOnStartup)
        {
            var existing = await db.AnalysisSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    snapshot => snapshot.Key == _snapshotOptions.DefaultSnapshotKey,
                    cancellationToken);

            if (existing?.Status == AnalysisSnapshotStatuses.Processed)
            {
                logger.LogInformation(
                    "Default snapshot {SnapshotKey} is already processed. Startup processing skipped.",
                    _snapshotOptions.DefaultSnapshotKey);
            }
            else
            {
                var processor = scope.ServiceProvider.GetRequiredService<SnapshotProcessingService>();
                await processor.ProcessAsync(
                    _snapshotOptions.DefaultSnapshotKey,
                    skipIfProcessed: true,
                    cancellationToken);
            }
        }
    }

    private static async Task EnsureMigrationsHistoryTableExistsAsync(
        FantasySentimentDbContext db,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
            );
            """,
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
