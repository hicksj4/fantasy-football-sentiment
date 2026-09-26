using System.Text.Json;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;

namespace FantasySentiment.Tests.Benchmark;

internal static class BenchmarkFixtureLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static BenchmarkCorpus LoadCorpus()
    {
        var repositoryRoot = FindRepositoryRoot();
        var fixturesDirectory = Path.Combine(
            repositoryRoot,
            "backend",
            "FantasySentiment.Tests",
            "Fixtures",
            "Benchmark");

        if (!Directory.Exists(fixturesDirectory))
        {
            throw new DirectoryNotFoundException($"Benchmark fixture directory not found: {fixturesDirectory}");
        }

        var fixtures = Directory
            .EnumerateFiles(fixturesDirectory, "week2-*.json")
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => LoadFixture(path))
            .ToList();

        foreach (var fixture in fixtures)
        {
            ValidateFixture(fixture);
        }

        return new BenchmarkCorpus
        {
            RepositoryRoot = repositoryRoot,
            Fixtures = fixtures,
            Players = LoadPlayers(repositoryRoot, fixtures),
            SentimentLexicon = LoadSentimentLexicon(repositoryRoot),
            IntentLexicon = LoadIntentLexicon(repositoryRoot)
        };
    }

    public static JsonSerializerOptions SerializerOptions => JsonOptions;

    private static BenchmarkFixture LoadFixture(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BenchmarkFixture>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Unable to parse benchmark fixture: {path}");
    }

    private static void ValidateFixture(BenchmarkFixture fixture)
    {
        if (string.IsNullOrWhiteSpace(fixture.Thread.Name))
        {
            throw new InvalidOperationException("Benchmark fixture is missing thread.name.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var comment in fixture.Comments)
        {
            if (string.IsNullOrWhiteSpace(comment.Id))
            {
                throw new InvalidOperationException($"Fixture {fixture.Thread.Name} contains a comment with no id.");
            }

            if (!ids.Add(comment.Id))
            {
                throw new InvalidOperationException(
                    $"Fixture {fixture.Thread.Name} contains duplicate comment id {comment.Id}.");
            }

            if (string.IsNullOrWhiteSpace(comment.Body))
            {
                throw new InvalidOperationException(
                    $"Fixture {fixture.Thread.Name} comment {comment.Id} has an empty body.");
            }
        }

        foreach (var comment in fixture.Comments.Where(comment => !string.IsNullOrWhiteSpace(comment.ParentId)))
        {
            if (!ids.Contains(comment.ParentId!))
            {
                throw new InvalidOperationException(
                    $"Fixture {fixture.Thread.Name} comment {comment.Id} references missing parent {comment.ParentId}.");
            }
        }
    }

    private static IReadOnlyList<Player> LoadPlayers(
        string repositoryRoot,
        IReadOnlyList<BenchmarkFixture> fixtures)
    {
        var byName = new Dictionary<string, BenchmarkPlayerSeed>(StringComparer.OrdinalIgnoreCase);
        var playerSeedPath = Path.Combine(
            repositoryRoot,
            "backend",
            "FantasySentiment.Api",
            "SeedData",
            "players.sample.json");
        var basePlayers = JsonSerializer.Deserialize<List<BenchmarkPlayerSeed>>(
                              File.ReadAllText(playerSeedPath),
                              JsonOptions)
                          ?? [];

        foreach (var player in basePlayers)
        {
            MergePlayer(byName, player);
        }

        foreach (var player in fixtures.SelectMany(fixture => fixture.Players))
        {
            MergePlayer(byName, player);
        }

        var id = 1;
        return byName.Values
            .OrderBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .Select(player => new Player
            {
                Id = id++,
                Name = player.Name,
                Team = player.Team,
                Position = player.Position,
                ExternalId = player.ExternalId,
                IsActive = true,
                Aliases = player.Aliases
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(alias => new PlayerAlias { Alias = alias })
                    .ToList()
            })
            .ToList();
    }

    private static SentimentLexiconOptions LoadSentimentLexicon(string repositoryRoot)
    {
        var root = LoadLexiconRoot(repositoryRoot);
        return root.GetProperty("SentimentLexicon").Deserialize<SentimentLexiconOptions>(JsonOptions)
               ?? new SentimentLexiconOptions();
    }

    private static IntentLexiconOptions LoadIntentLexicon(string repositoryRoot)
    {
        var root = LoadLexiconRoot(repositoryRoot);
        return root.GetProperty("IntentLexicon").Deserialize<IntentLexiconOptions>(JsonOptions)
               ?? new IntentLexiconOptions();
    }

    private static JsonElement LoadLexiconRoot(string repositoryRoot)
    {
        var lexiconPath = Path.Combine(
            repositoryRoot,
            "backend",
            "FantasySentiment.Api",
            "SeedData",
            "sentiment-terms.json");
        using var document = JsonDocument.Parse(File.ReadAllText(lexiconPath), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        return document.RootElement.Clone();
    }

    private static void MergePlayer(
        Dictionary<string, BenchmarkPlayerSeed> players,
        BenchmarkPlayerSeed incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.Name))
        {
            return;
        }

        if (!players.TryGetValue(incoming.Name, out var existing))
        {
            players[incoming.Name] = new BenchmarkPlayerSeed
            {
                Name = incoming.Name,
                Team = incoming.Team,
                Position = incoming.Position,
                ExternalId = incoming.ExternalId,
                Aliases = incoming.Aliases
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
            return;
        }

        if (string.IsNullOrWhiteSpace(existing.Team))
        {
            existing.Team = incoming.Team;
        }

        if (string.IsNullOrWhiteSpace(existing.Position))
        {
            existing.Position = incoming.Position;
        }

        foreach (var alias in incoming.Aliases)
        {
            if (!existing.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                existing.Aliases.Add(alias);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "backend", "FantasySentiment.Api"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend", "FantasySentiment.Tests"))
                && Directory.Exists(Path.Combine(directory.FullName, "frontend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate fantasy-sentiment repository root.");
    }
}
