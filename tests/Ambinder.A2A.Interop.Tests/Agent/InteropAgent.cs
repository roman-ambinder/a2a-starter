using System.Collections.Concurrent;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;

namespace Ambinder.A2A.Interop.Tests.Agent;

/// <summary>
/// Holds the wait of every <c>slow</c> task until a <c>release</c> message or a cancel ends it. A singleton in the host.
/// No timers: the wait is a <see cref="TaskCompletionSource"/>. A release that arrives before the task registers still works.
/// </summary>
public sealed class SlowTaskGate
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _gates = new(StringComparer.Ordinal);

    private TaskCompletionSource Gate(string taskId)
        => _gates.GetOrAdd(taskId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    /// <summary>Waits until <see cref="Release"/> is called for <paramref name="taskId"/> or <paramref name="ct"/> is canceled.</summary>
    public async Task WaitAsync(string taskId, CancellationToken ct)
    {
        try { await Gate(taskId).Task.WaitAsync(ct).ConfigureAwait(false); }
        finally { _gates.TryRemove(taskId, out _); }
    }

    /// <summary>Lets the slow task <paramref name="taskId"/> finish.</summary>
    public void Release(string taskId) => Gate(taskId).TrySetResult();
}

/// <summary>
/// The .NET side of the interop agent contract (<c>interop/contract.md</c>). Reads the first text part of the user message and acts
/// on its first word: <c>echo</c>, <c>task</c>, <c>input</c>, <c>slow</c>, <c>release</c>, <c>chunks</c>, <c>fail</c>, <c>data</c>, <c>raw</c>, <c>ext</c>.
/// </summary>
/// <remarks>
/// <c>release</c>: this server accepts a follow-up on a task only while the task waits for input, so a <c>release</c> cannot be sent
/// "on" a working task. It is a new message that names the slow task, either as <c>release &lt;taskId&gt;</c> or in
/// <c>message.referenceTaskIds</c>; it is answered with a message and starts no task.
/// </remarks>
public sealed class InteropAgent(SlowTaskGate gate) : IAgentHandler
{
    /// <inheritdoc />
    public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        var text = context.Message.Parts.FirstOrDefault(p => p.Text is not null)?.Text ?? "";
        var space = text.IndexOf(' ');
        var command = (space < 0 ? text : text[..space]).Trim();
        var rest = space < 0 ? "" : text[(space + 1)..];

        // The only way a task is continued is after `input`: the follow-up completes it with the received text.
        if (context.CurrentTask is not null)
        {
            await sink.AddArtifactAsync(TextArtifact(text), false, true, ct);
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
            return;
        }

        switch (command)
        {
            case "echo":
                await sink.ReplyAsync(AgentMessage(Part.FromText(rest)), ct);
                break;
            case "task":
                await sink.AddArtifactAsync(TextArtifact(rest), false, true, ct);
                await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
                break;
            case "input":
                await sink.UpdateStatusAsync(TaskState.InputRequired, AgentMessage(Part.FromText("input required")), ct);
                break;
            case "slow":
                await sink.UpdateStatusAsync(TaskState.Working, null, ct);
                await gate.WaitAsync(context.TaskId, ct); // canceled: throws, the server ends the task through CancelAsync
                await sink.AddArtifactAsync(TextArtifact("released"), false, true, ct);
                await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
                break;
            case "release":
                var ids = context.Message.ReferenceTaskIds.Concat(rest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct().ToArray();
                foreach (var id in ids) gate.Release(id);
                await sink.ReplyAsync(AgentMessage(Part.FromText($"released {ids.Length}")), ct);
                break;
            case "chunks":
                if (!int.TryParse(rest.Trim(), out var n) || n < 1)
                {
                    await sink.UpdateStatusAsync(TaskState.Failed, AgentMessage(Part.FromText("chunks needs a positive count")), ct);
                    break;
                }
                await sink.UpdateStatusAsync(TaskState.Working, null, ct);
                var artifactId = Guid.NewGuid().ToString("N");
                for (var i = 0; i < n; i++)
                    await sink.AddArtifactAsync(new Artifact { ArtifactId = artifactId, Parts = [Part.FromText($"chunk-{i + 1}")] }, append: i > 0, lastChunk: i == n - 1, ct);
                await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
                break;
            case "fail":
                await sink.UpdateStatusAsync(TaskState.Failed, AgentMessage(Part.FromText("interop failure")), ct);
                break;
            case "data":
                await sink.ReplyAsync(AgentMessage(Part.FromData(ContractPayloads.DataElement())), ct);
                break;
            case "raw":
                await sink.ReplyAsync(AgentMessage(Part.FromRaw(ContractPayloads.RawBytes.ToArray(), ContractPayloads.RawMediaType, ContractPayloads.RawFilename)), ct);
                break;
            case "ext":
                await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
                break;
            default:
                await sink.UpdateStatusAsync(TaskState.Failed, AgentMessage(Part.FromText($"unknown command: {command}")), ct);
                break;
        }
    }

    internal static Message AgentMessage(Part part)
        => new() { MessageId = Guid.NewGuid().ToString("N"), Role = Role.Agent, Parts = [part] };

    private static Artifact TextArtifact(string text)
        => new() { ArtifactId = Guid.NewGuid().ToString("N"), Parts = [Part.FromText(text)] };
}
