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
        var path = Path.Combine(environment.ContentRootPath, "SeedData", "players.sample.json");
        await using var stream = File.OpenRead(path);
        var players = await JsonSerializer.DeserializeAsync<List<PlayerSeedDto>>(stream, JsonOptions, cancellationToken)
                      ?? [];

        var existingPlayers = await db.Players
            .Include(player => player.Aliases)
            .ToDictionaryAsync(player => player.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var addedPlayers = 0;
        var addedAliases = 0;

        foreach (var seed in players)
        {
            if (!existingPlayers.TryGetValue(seed.Name, out var player))
            {
                player = new Player
                {
                    Name = seed.Name,
                    Team = seed.Team,
                    Position = seed.Position,
                    ExternalId = seed.ExternalId,
                    IsActive = true
                };
                db.Players.Add(player);
                existingPlayers[seed.Name] = player;
                addedPlayers++;
            }
            else
            {
                player.Team = seed.Team;
                player.Position = seed.Position;
                player.ExternalId ??= seed.ExternalId;
                player.IsActive = true;
            }

            foreach (var alias in seed.Aliases.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (player.Aliases.Any(existing => existing.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                player.Aliases.Add(new PlayerAlias { Alias = alias });
                addedAliases++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Seeded player catalog. Added players: {AddedPlayers}. Added aliases: {AddedAliases}. Total seed players: {SeedPlayerCount}.",
            addedPlayers,
            addedAliases,
            players.Count);
    }

    private sealed record PlayerSeedDto(
        string Name,
        string Team,
        string Position,
        string? ExternalId,
        string[] Aliases);
}
