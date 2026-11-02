using A2A.Protocol;
using A2A.Server.Agents;

namespace AgentServer.Agents;

/// <summary>Placeholder for every demo agent until Tasks 15–16: completes the task with "not implemented" in the status message.</summary>
public sealed class StubAgent : IAgentHandler
{
    public ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
        => sink.UpdateStatusAsync(TaskState.Completed, new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = Role.Agent,
            TaskId = context.TaskId,
            ContextId = context.ContextId,
            Parts = [Part.FromText($"Agent '{context.AgentName}' is not implemented yet.")],
        }, ct);
}
