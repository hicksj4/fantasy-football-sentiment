namespace FantasySentiment.Api.Options;

public sealed class SnapshotOptions
{
    public int IntervalMinutes { get; set; } = 5;
    public int RecentWindowMinutes { get; set; } = 60;
    public int BaselineWindowHours { get; set; } = 24;
}
