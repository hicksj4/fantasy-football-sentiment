namespace FantasySentiment.Api.Services;

public static class RobertaSentimentScoreMapper
{
    public static readonly string[] Labels =
    [
        "strongly negative",
        "negative",
        "negative or neutral",
        "positive",
        "strongly positive"
    ];

    public static readonly double[] ScoreWeights = [-1.0, -0.5, 0.0, 0.5, 1.0];

    public static RobertaScoreResult Map(float[] logits)
    {
        if (logits.Length != Labels.Length)
        {
            throw new InvalidOperationException($"Expected {Labels.Length} RoBERTa logits, but received {logits.Length}.");
        }

        var probabilities = Softmax(logits);
        var score = 0.0;
        var winningIndex = 0;

        for (var i = 0; i < probabilities.Length; i++)
        {
            score += probabilities[i] * ScoreWeights[i];
            if (probabilities[i] > probabilities[winningIndex])
            {
                winningIndex = i;
            }
        }

        var probabilityMap = Labels
            .Select((label, index) => new KeyValuePair<string, double>(label, Math.Round(probabilities[index], 6)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        return new RobertaScoreResult(
            Math.Round(Math.Clamp(score, -1, 1), 3),
            Labels[winningIndex],
            Math.Round(probabilities[winningIndex], 6),
            probabilityMap);
    }

    private static double[] Softmax(float[] logits)
    {
        var max = logits.Max();
        var exp = new double[logits.Length];
        var sum = 0.0;

        for (var i = 0; i < logits.Length; i++)
        {
            exp[i] = Math.Exp(logits[i] - max);
            sum += exp[i];
        }

        for (var i = 0; i < exp.Length; i++)
        {
            exp[i] /= sum;
        }

        return exp;
    }
}

public sealed record RobertaScoreResult(
    double Score,
    string WinningLabel,
    double WinningProbability,
    IReadOnlyDictionary<string, double> Probabilities);
