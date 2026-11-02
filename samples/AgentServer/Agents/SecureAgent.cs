using System.Text.Json;
using System.Text.RegularExpressions;
using A2A.Protocol;
using A2A.Server.Agents;

namespace AgentServer.Agents;

/// <summary>
/// Approves a report (<c>approve &lt;id&gt;</c>). Needs the role <c>Reports.Approve</c>: without it the task pauses in AUTH_REQUIRED.
/// A later message on the same task from a caller with the role completes it with the <c>approval</c> artifact. The id comes from
/// the first <c>approve</c> message in the stored history; <c>approvedBy</c> is the caller's <c>azp</c> claim.
/// </summary>
public sealed partial class SecureAgent : IAgentHandler
{
    public const string ArtifactId = "approval";
    public const string ApproveRole = "Reports.Approve";
    public const string AuthRequiredText = "Role Reports.Approve required. Retry with an approver token.";

    [GeneratedRegex(@"^\s*approve\s+(?<id>\S+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApprovePattern();

    public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        var id = FindId(context);
        if (id is null)
        {
            await Say(context, sink, TaskState.Rejected, "Try 'approve <id>'.", ct);
            return;
        }

        await sink.UpdateStatusAsync(TaskState.Working, null, ct);

        if (!context.User.IsInRole(ApproveRole))
        {
            await Say(context, sink, TaskState.AuthRequired, AuthRequiredText, ct);
            return;
        }

        var approvedBy = context.User.FindFirst("azp")?.Value ?? context.User.Identity?.Name ?? "unknown";
        using var data = JsonSerializer.SerializeToDocument(new Dictionary<string, string> { ["id"] = id, ["approvedBy"] = approvedBy });
        await sink.AddArtifactAsync(new Artifact { ArtifactId = ArtifactId, Parts = [Part.FromData(data.RootElement.Clone())] },
            append: false, lastChunk: true, ct);
        await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
    }

    private static string? FindId(AgentRequestContext context)
    {
        IEnumerable<Message> messages = (context.CurrentTask?.History ?? []).Where(m => m.Role == Role.User).Append(context.Message);
        foreach (var message in messages)
        {
            var text = message.Parts.FirstOrDefault(p => p.Text is not null)?.Text ?? "";
            var match = ApprovePattern().Match(text);
            if (match.Success) return match.Groups["id"].Value;
        }
        return null;
    }

    private static ValueTask Say(AgentRequestContext context, IAgentEventSink sink, TaskState state, string text, CancellationToken ct)
        => sink.UpdateStatusAsync(state, new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = Role.Agent,
            TaskId = context.TaskId,
            ContextId = context.ContextId,
            Parts = [Part.FromText(text)],
        }, ct);
}
