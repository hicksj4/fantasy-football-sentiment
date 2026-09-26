namespace FantasySentiment.Api.Options;

public sealed class VoteWeightingOptions
{
    public double PositiveLogDivisor { get; set; } = 2.0;
    public double PositiveMaximumWeight { get; set; } = 6.0;
    public double NegativeMinimumWeight { get; set; } = 0.10;
    public int NeutralMaximumScore { get; set; } = 1;
}
