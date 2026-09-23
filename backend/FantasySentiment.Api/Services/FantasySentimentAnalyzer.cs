using System.Text.RegularExpressions;
using FantasySentiment.Api.Domain;
using FantasySentiment.Api.Options;
using Microsoft.Extensions.Options;

namespace FantasySentiment.Api.Services;

public sealed class FantasySentimentAnalyzer(IOptionsMonitor<SentimentLexiconOptions> options)
{
    public SentimentResult Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SentimentResult(0, SentimentCategories.Neutral);
        }

        var lexicon = options.CurrentValue;
        var rawScore = 0.0;

        foreach (var term in lexicon.PositiveTerms)
        {
            rawScore += ScoreTerm(text, term, 1, lexicon);
        }

        foreach (var term in lexicon.NegativeTerms)
        {
            rawScore += ScoreTerm(text, term, -1, lexicon);
        }

        var normalizedScore = Math.Round(Math.Tanh(rawScore / 3.0), 3);
        var category = normalizedScore switch
        {
            >= 0.15 => SentimentCategories.Positive,
            <= -0.15 => SentimentCategories.Negative,
            _ => SentimentCategories.Neutral
        };

        return new SentimentResult(normalizedScore, category);
    }

    private static double ScoreTerm(
        string text,
        string term,
        int polarity,
        SentimentLexiconOptions lexicon)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return 0;
        }

        var score = 0.0;
        var normalizedTerm = NormalizePhrase(term);
        var pattern = $@"(?<![A-Za-z0-9]){Regex.Escape(normalizedTerm).Replace("\\ ", "\\s+")}(?![A-Za-z0-9])";
        var normalizedText = NormalizePhrase(text);

        foreach (Match match in Regex.Matches(normalizedText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var before = WordsBefore(normalizedText, match.Index, 3);
            var hasNegator = before.Any(word => lexicon.Negators.Contains(word, StringComparer.OrdinalIgnoreCase));
            var hasIntensifier = before.Any(word => lexicon.Intensifiers.Contains(word, StringComparer.OrdinalIgnoreCase));

            var weight = hasIntensifier ? 1.35 : 1.0;
            score += (hasNegator ? -polarity : polarity) * weight;
        }

        return score;
    }

    private static string NormalizePhrase(string value)
    {
        var lowered = value.ToLowerInvariant();
        var normalized = Regex.Replace(lowered, @"[^a-z0-9']+", " ");
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private static IReadOnlyList<string> WordsBefore(string text, int index, int count)
    {
        var prefix = text[..Math.Max(0, index)];
        return Regex.Matches(prefix, @"[a-z0-9']+")
            .Select(match => match.Value)
            .TakeLast(count)
            .ToList();
    }
}

public sealed record SentimentResult(double Score, string Category);
