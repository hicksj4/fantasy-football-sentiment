using System.Text.Json;
using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FantasySentiment.Api.Services;

public sealed class DemoDataSeeder(
    FantasySentimentDbContext db,
    IWebHostEnvironment environment,
    ILogger<DemoDataSeeder> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SeedAsync(CancellationToken cancellationToken)
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

    private sealed record PlayerSeedDto(
        string Name,
        string Team,
        string Position,
        string? ExternalId,
        string[] Aliases);
}
