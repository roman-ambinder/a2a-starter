using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;

namespace Ambinder.A2A.Interop.Tests.Agent;

/// <summary>
/// The second agent. Its card declares <see cref="ContractPayloads.RequiredExtension"/> as required, so the server rejects any call
/// without it. Every accepted message completes a task.
/// </summary>
public sealed class RequiredExtensionAgent : IAgentHandler
{
    /// <inheritdoc />
    public ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
        => sink.UpdateStatusAsync(TaskState.Completed, null, ct);
}
