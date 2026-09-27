using System.Text.RegularExpressions;

namespace FantasySentiment.Api.Services;

public sealed class MentionContextExtractor
{
    private static readonly Regex SentenceRegex = new(
        @"[^.!?\r\n]+(?:[.!?]+|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ClauseSeparatorRegex = new(
        @"\s+(?:but|however|though|while|whereas|yet)\s+|;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public string ExtractContext(string fullText, PlayerMentionMatch mention)
    {
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return string.Empty;
        }

        var sentences = SentenceRegex
            .Matches(fullText)
            .Select(match =>
            {
                var text = match.Value.Trim();
                var leadingWhitespace = match.Value.Length - match.Value.TrimStart().Length;
                return new SentenceSpan(
                    match.Index + leadingWhitespace,
                    text.Length,
                    text);
            })
            .Where(sentence => sentence.Text.Length > 0)
            .ToList();

        if (sentences.Count == 0)
        {
            return fullText.Trim();
        }

        var mentionIndex = Math.Clamp(mention.StartIndex, 0, Math.Max(0, fullText.Length - 1));
        var sentenceIndex = sentences.FindIndex(sentence =>
            mentionIndex >= sentence.StartIndex
            && mentionIndex <= sentence.StartIndex + sentence.Length);

        if (sentenceIndex < 0)
        {
            return fullText.Trim();
        }

        var targetSentence = sentences[sentenceIndex];
        var targetText = ExtractTargetClause(fullText, targetSentence, mention) ?? targetSentence.Text;
        var parts = new List<string> { targetText };
        if (sentenceIndex + 1 < sentences.Count && IsLikelyContinuation(sentences[sentenceIndex + 1].Text))
        {
            parts.Add(sentences[sentenceIndex + 1].Text);
        }

        return string.Join(' ', parts).Trim();
    }

    private static string? ExtractTargetClause(
        string fullText,
        SentenceSpan sentence,
        PlayerMentionMatch mention)
    {
        var separators = ClauseSeparatorRegex.Matches(sentence.Text);
        if (separators.Count == 0)
        {
            return null;
        }

        var clauseStart = 0;
        var mentionIndex = Math.Clamp(mention.StartIndex, 0, Math.Max(0, fullText.Length - 1));
        for (var i = 0; i <= separators.Count; i++)
        {
            var separator = i < separators.Count ? separators[i] : null;
            var clauseEnd = separator?.Index ?? sentence.Text.Length;
            var absoluteStart = sentence.StartIndex + clauseStart;
            var absoluteEnd = sentence.StartIndex + clauseEnd;
            if (mentionIndex >= absoluteStart && mentionIndex <= absoluteEnd)
            {
                var clause = sentence.Text[clauseStart..clauseEnd].Trim();
                return clause.Length == 0 ? null : clause;
            }

            if (separator is null)
            {
                break;
            }

            clauseStart = separator.Index + separator.Length;
        }

        return null;
    }

    private static bool IsLikelyContinuation(string sentence)
    {
        var lower = sentence.Trim().ToLowerInvariant();
        return lower.StartsWith("he's ")
               || lower.StartsWith("he is ")
               || lower.StartsWith("they're ")
               || lower.StartsWith("they are ")
               || lower.StartsWith("still ")
               || lower.StartsWith("also ")
               || lower.StartsWith("that ")
               || lower.StartsWith("this ");
    }

    private sealed record SentenceSpan(int StartIndex, int Length, string Text);
}
