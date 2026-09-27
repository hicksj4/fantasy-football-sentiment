using FantasySentiment.Api.Domain;

namespace FantasySentiment.Api.Services;

public interface ISentimentAnalyzer
{
    ValueTask<TargetedSentimentResult> AnalyzeAsync(
        TargetedSentimentRequest request,
        CancellationToken cancellationToken);
}

public sealed record TargetedSentimentRequest(
    string FullText,
    string Context,
    PlayerMentionMatch Mention,
    string TargetText = "",
    string TargetPerspective = TargetPerspectives.Unknown,
    string TargetAttributionMethod = TargetAttributionMethods.Explicit);

public sealed record SentimentAnalysisResult(
    string TargetText,
    string Perspective,
    string VaderText,
    double RawVaderScore,
    double FantasyAdjustment,
    double EmojiAdjustment,
    double StructuralAdjustment,
    double PerspectiveAdjustment,
    double QuestionAdjustment,
    double SemanticConfidence,
    double FinalScore,
    IReadOnlyList<string> MatchedPositiveTerms,
    IReadOnlyList<string> MatchedNegativeTerms,
    IReadOnlyList<string> MatchedPhrases,
    IReadOnlyList<string> MatchedEmoji,
    IReadOnlyList<string> AppliedRules)
{
    public double TotalAdjustment => Math.Round(FinalScore - RawVaderScore, 3);

    public string ToDiagnostics()
    {
        return string.Join(
            "; ",
            new[]
            {
                $"vader={RawVaderScore:0.###}",
                $"fantasy={FantasyAdjustment:0.###}",
                $"emoji={EmojiAdjustment:0.###}",
                $"structural={StructuralAdjustment:0.###}",
                $"perspective={PerspectiveAdjustment:0.###}",
                $"question={QuestionAdjustment:0.###}",
                $"semanticConfidence={SemanticConfidence:0.###}",
                $"final={FinalScore:0.###}",
                $"perspectiveLabel={Perspective}",
                $"positiveTerms={Join(MatchedPositiveTerms)}",
                $"negativeTerms={Join(MatchedNegativeTerms)}",
                $"phrases={Join(MatchedPhrases)}",
                $"emojiMatches={Join(MatchedEmoji)}",
                $"rules={Join(AppliedRules)}"
            });
    }

    public string ToDiagnosticTrace(TargetedSentimentRequest request)
    {
        return string.Join(
            "; ",
            new[]
            {
                $"target={Compact(request.Mention.PlayerName, 80)}",
                $"alias={Compact(request.Mention.MatchedAlias, 80)}",
                $"targetText={Compact(TargetText, 180)}",
                $"vaderText={Compact(VaderText, 180)}",
                $"perspectiveLabel={Perspective}",
                $"targetAttribution={request.TargetAttributionMethod}",
                ToDiagnostics()
            });
    }

    private static string Join(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? "none" : string.Join('|', values);
    }

    private static string Compact(string value, int maxLength)
    {
        var compact = string.Join(
            ' ',
            value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        if (compact.Length <= maxLength)
        {
            return compact;
        }

        return $"{compact[..Math.Max(0, maxLength - 3)]}...";
    }
}

public sealed record TargetedSentimentResult(
    double RawSentimentScore,
    double FinalSentimentScore,
    string SentimentCategory,
    double RuleBasedScore,
    double? ModelScore,
    double FantasyAdjustment,
    string AnalyzerName,
    string Context,
    string? Diagnostics = null)
{
    public static string CategoryFromScore(double score)
    {
        return score switch
        {
            >= 0.15 => SentimentCategories.Positive,
            <= -0.15 => SentimentCategories.Negative,
            _ => SentimentCategories.Neutral
        };
    }
}

