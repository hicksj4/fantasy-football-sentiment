using System.Text;

namespace FantasySentiment.Api.Services;

public sealed class ThreadContextBuilder
{
    public string Build(ResolvedTargetContext context, int maxLength = 1200)
    {
        return Build(
            context.ThreadTitle,
            context.ParentComment,
            context.CurrentComment,
            context.TargetText,
            context.Target.PlayerName,
            maxLength);
    }

    public string Build(
        string postTitle,
        string? parentComment,
        string commentBody,
        string targetContext,
        string targetPlayer,
        int maxLength = 1200)
    {
        var builder = new StringBuilder();
        Append(builder, "Thread title", postTitle);
        Append(builder, "Parent", parentComment);
        Append(builder, "Comment", commentBody);
        Append(builder, "Target context", targetContext);
        Append(builder, "Target", targetPlayer);

        var context = builder.ToString().Trim();
        return context.Length <= maxLength ? context : context[^maxLength..];
    }

    private static void Append(StringBuilder builder, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append(label);
        builder.Append(": ");
        builder.Append(value.Trim());
    }
}
