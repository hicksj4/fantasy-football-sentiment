namespace FantasySentiment.Api.Services;

public sealed class BuzzScoreCalculator
{
    public double Calculate(
        int currentMentions,
        TimeSpan currentWindow,
        int baselineMentions,
        TimeSpan baselineWindow)
    {
        if (currentWindow <= TimeSpan.Zero || baselineWindow <= TimeSpan.Zero)
        {
            return 1;
        }

        var currentRate = (currentMentions + 1.0) / Math.Max(0.25, currentWindow.TotalHours);
        var baselineRate = (baselineMentions + 5.0) / Math.Max(1, baselineWindow.TotalHours);
        var buzz = currentRate / baselineRate;

        if (currentMentions < 3)
        {
            buzz = Math.Min(buzz, 1.5);
        }

        return Math.Round(Math.Clamp(buzz, 0, 99), 2);
    }
}
