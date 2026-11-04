using System.Globalization;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;

namespace Ambinder.A2A.Benchmarks.Agents;

// In-memory only: no LLM, database or network (spec 14 section 7).

/// <summary>Replies with one message and creates no task.</summary>
public sealed class HelloAgent : IAgentHandler
{
    private static readonly Message Reply = new()
    {
        MessageId = "hello-reply",
        Role = Role.Agent,
        Parts = [Part.FromText("Hello from the benchmark agent.")],
    };

    public ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
        => sink.ReplyAsync(Reply, ct);
}

/// <summary>
/// Creates a task, emits n updates (status and artifact, alternating, starting with status) and completes it.
/// n is the text of the first part of the user message.
/// </summary>
public sealed class TaskAgent : IAgentHandler
{
    public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        var n = int.Parse(context.Message.Parts[0].Text ?? "0", CultureInfo.InvariantCulture);
        await sink.UpdateStatusAsync(TaskState.Working, null, ct).ConfigureAwait(false);
        for (var i = 0; i < n; i++)
        {
            if (i % 2 == 0)
            {
                await sink.UpdateStatusAsync(TaskState.Working, null, ct).ConfigureAwait(false);
            }
            else
            {
                await sink.AddArtifactAsync(
                    new Artifact { ArtifactId = "a" + i.ToString(CultureInfo.InvariantCulture), Parts = [Part.FromText("chunk")] },
                    append: false, lastChunk: true, ct).ConfigureAwait(false);
            }
        }
        await sink.UpdateStatusAsync(TaskState.Completed, null, ct).ConfigureAwait(false);
    }
}
