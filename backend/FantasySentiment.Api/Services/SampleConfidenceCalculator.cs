namespace FantasySentiment.Api.Services;

public static class SampleConfidenceCalculator
{
    public static SampleConfidence Calculate(int mentionCount, double totalVoteWeight)
    {
        var mentionComponent = Math.Min(1, mentionCount / 50.0) * 0.6;
        var voteWeightComponent = Math.Min(1, totalVoteWeight / 100.0) * 0.4;
        var sampleStrength = Math.Round(Math.Clamp(mentionComponent + voteWeightComponent, 0, 1), 3);
        var confidence = sampleStrength switch
        {
            >= 0.7 when mentionCount >= 25 => "High",
            >= 0.25 when mentionCount >= 6 => "Medium",
            _ => "Low"
        };

        return new SampleConfidence(sampleStrength, confidence);
    }
}

public sealed record SampleConfidence(double SampleStrengthScore, string Confidence);
