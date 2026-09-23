namespace FantasySentiment.Api.Services;

public sealed class VoteWeightCalculator
{
    public double Calculate(int redditScore)
    {
        if (redditScore == 0)
        {
            return 1;
        }

        if (redditScore < 0)
        {
            var downvotedWeight = 1.0 / (1.0 + Math.Log(1 + Math.Abs(redditScore)) / 2.5);
            return Math.Round(Math.Clamp(downvotedWeight, 0.25, 1), 3);
        }

        var upvotedWeight = 1.0 + Math.Log(1 + redditScore) / 2.5;
        return Math.Round(Math.Clamp(upvotedWeight, 1, 4), 3);
    }
}
