using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Tasks;

/// <summary>A task together with the agent and owner it belongs to.</summary>
public sealed record StoredTask(string AgentName, string Owner, string? Tenant, AgentTask Task);

/// <summary>Stores tasks, keyed by agent name and task id.</summary>
public interface ITaskStore
{
    /// <summary>Returns a copy of the stored task, or null if it does not exist.</summary>
    ValueTask<StoredTask?> GetAsync(string agentName, string taskId, CancellationToken ct);

    /// <summary>Saves a copy of the task, replacing any existing task with the same agent and id.</summary>
    ValueTask SaveAsync(StoredTask task, CancellationToken ct);

    /// <summary>
    /// Lists the owner's tasks for one agent, newest status timestamp first, one page at a time.
    /// Throws <see cref="A2AException"/> with <see cref="A2AErrorCode.InvalidParams"/> for a bad page size, history length or page token.
    /// </summary>
    ValueTask<ListTasksResponse> ListAsync(string agentName, string owner, ListTasksRequest request, CancellationToken ct);
}
