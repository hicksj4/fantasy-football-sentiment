namespace FantasySentiment.Api.Options;

public sealed class SnapshotProcessingOptions
{
    public string DefaultSnapshotKey { get; set; } = "2026-week-4";
    public bool ProcessMockSnapshotOnStartup { get; set; }
    public string FixtureDirectory { get; set; } = "SeedData/Snapshots";
}
