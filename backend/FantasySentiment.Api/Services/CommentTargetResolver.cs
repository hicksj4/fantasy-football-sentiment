using FantasySentiment.Api.Domain;

namespace FantasySentiment.Api.Services;

public interface ICommentTargetResolver
{
    CommentTargetResolution Resolve(CommentTargetResolutionRequest request);
}

public sealed class CommentTargetResolver(
    PlayerMentionDetector mentionDetector,
    ILogger<CommentTargetResolver> logger) : ICommentTargetResolver
{
    public CommentTargetResolution Resolve(CommentTargetResolutionRequest request)
    {
        var explicitMatches = mentionDetector.DetectMentions(request.CommentBody, request.Players);
        if (explicitMatches.Count > 0)
        {
            return CommentTargetResolution.Resolved(explicitMatches
                .Select(match => ResolvedCommentTarget.FromMatch(
                    match,
                    TargetAttributionMethods.Explicit,
                    1.0))
                .ToArray());
        }

        if (request.ParentTargets.Count == 1)
        {
            var parentTarget = request.ParentTargets[0];
            var method = parentTarget.AttributionMethod == TargetAttributionMethods.Explicit
                ? TargetAttributionMethods.ParentInherited
                : TargetAttributionMethods.AncestorInherited;
            var confidence = method == TargetAttributionMethods.ParentInherited ? 0.90 : 0.80;
            return CommentTargetResolution.Resolved([
                parentTarget.InheritForCurrentComment(method, confidence)
            ]);
        }

        if (request.ParentTargets.Count > 1)
        {
            return CommentTargetResolution.Unresolved(
                "ambiguous inherited targets",
                request.ParentTargets.Select(target => target.PlayerName).ToArray());
        }

        var titleMatches = mentionDetector.DetectMentions(request.PostTitle, request.Players);
        if (titleMatches.Count == 1 && IsLikelyDirectReaction(request.CommentBody))
        {
            return CommentTargetResolution.Resolved([
                ResolvedCommentTarget.FromMatch(
                    titleMatches[0].ForInheritedComment(),
                    TargetAttributionMethods.ThreadTitleInherited,
                    0.70)
            ]);
        }

        if (titleMatches.Count > 1)
        {
            return CommentTargetResolution.Unresolved(
                "ambiguous thread title targets",
                titleMatches.Select(match => match.PlayerName).ToArray());
        }

        logger.LogDebug(
            "Skipped comment {CommentId}: no explicit, parent, or title target.",
            request.CommentRedditId);
        return CommentTargetResolution.Unresolved("no target");
    }

    private static bool IsLikelyDirectReaction(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // A top-level comment in a single-player thread is usually a direct reaction.
        // Keep it bounded to avoid attributing long side conversations to the title subject.
        return text.Trim().Length <= 220;
    }
}

public sealed record CommentTargetResolutionRequest(
    string CommentRedditId,
    string CommentBody,
    string PostTitle,
    IReadOnlyList<Player> Players,
    IReadOnlyList<ResolvedCommentTarget> ParentTargets);

public sealed record CommentTargetResolution(
    IReadOnlyList<ResolvedCommentTarget> Targets,
    string? UnresolvedReason,
    IReadOnlyList<string> CandidateNames)
{
    public static CommentTargetResolution Resolved(IReadOnlyList<ResolvedCommentTarget> targets)
    {
        return new CommentTargetResolution(targets, null, []);
    }

    public static CommentTargetResolution Unresolved(
        string reason,
        IReadOnlyList<string>? candidateNames = null)
    {
        return new CommentTargetResolution([], reason, candidateNames ?? []);
    }
}

public sealed record ResolvedCommentTarget(
    int PlayerId,
    string PlayerName,
    string Team,
    string Position,
    string MatchedAlias,
    int StartIndex,
    int Length,
    string AttributionMethod,
    double Confidence)
{
    public static ResolvedCommentTarget FromMatch(
        PlayerMentionMatch match,
        string attributionMethod,
        double confidence)
    {
        return new ResolvedCommentTarget(
            match.PlayerId,
            match.PlayerName,
            match.Team,
            match.Position,
            match.MatchedAlias,
            match.StartIndex,
            match.Length,
            attributionMethod,
            confidence);
    }

    public ResolvedCommentTarget InheritForCurrentComment(string method, double confidence)
    {
        return this with
        {
            MatchedAlias = PlayerName,
            StartIndex = 0,
            Length = 0,
            AttributionMethod = method,
            Confidence = confidence
        };
    }

    public PlayerMentionMatch ToMatch()
    {
        return new PlayerMentionMatch(
            PlayerId,
            PlayerName,
            Team,
            Position,
            MatchedAlias,
            StartIndex,
            Length);
    }
}

public static class PlayerMentionMatchExtensions
{
    public static PlayerMentionMatch ForInheritedComment(this PlayerMentionMatch match)
    {
        return match with
        {
            MatchedAlias = match.PlayerName,
            StartIndex = 0,
            Length = 0
        };
    }
}
