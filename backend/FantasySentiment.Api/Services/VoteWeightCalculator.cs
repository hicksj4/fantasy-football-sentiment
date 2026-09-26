using FantasySentiment.Api.Options;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Services;

public sealed class VoteWeightCalculator(IOptions<VoteWeightingOptions> options)
{
    private readonly VoteWeightingOptions _options = options.Value;

    public VoteWeightCalculator()
        : this(Microsoft.Extensions.Options.Options.Create(new VoteWeightingOptions()))
    {
    }

    public double Calculate(int redditScore)
    {
        if (redditScore >= 0 && redditScore <= _options.NeutralMaximumScore)
        {
            return 1;
        }

        if (redditScore < 0)
        {
            var rejectedWeight = 1.0 / Math.Sqrt(1 + Math.Abs(redditScore));
            return Math.Round(Math.Clamp(rejectedWeight, _options.NegativeMinimumWeight, 1), 3);
        }

        var upvotedWeight = 1.0 + Math.Log(redditScore) / _options.PositiveLogDivisor;
        return Math.Round(Math.Clamp(upvotedWeight, 1, _options.PositiveMaximumWeight), 3);
    }
}
