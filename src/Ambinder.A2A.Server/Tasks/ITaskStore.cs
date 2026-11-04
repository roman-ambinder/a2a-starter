using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Tasks;

/// <summary>A task together with the agent and owner it belongs to.</summary>
/// <param name="AgentName">The name of the agent the task belongs to.</param>
/// <param name="Owner">The owner key of the task, from the registered <see cref="Security.IOwnerResolver"/>.</param>
/// <param name="Tenant">The tenant the caller named, or <see langword="null"/>.</param>
/// <param name="Task">The task, with its status, history and artifacts.</param>
/// <remarks>The tenant is stored as the caller sent it. This version does not separate tasks by tenant.</remarks>
public sealed record StoredTask(string AgentName, string Owner, string? Tenant, AgentTask Task);

/// <summary>Stores tasks, keyed by agent name and task id.</summary>
/// <remarks>
/// Stored task history is append-only: a save may add history entries but never remove or change earlier ones.
/// The server relies on this to detect late events from a paused run.
/// <para>
/// The default store is registered as a singleton and keeps tasks in memory. It is added with <c>TryAdd</c>, so register your own
/// before <c>AddA2AServer</c> to replace it. Calls for different tasks overlap, so an implementation must be thread-safe.
/// For one task the server applies one change at a time: it holds a per-task lock around each read-change-save, so one server
/// process never saves the same task from two threads at once. It does not hold the lock for <see cref="ListAsync"/>.
/// A save replaces the whole stored task. A read returns a copy that the server may change without affecting the store.
/// <see cref="ListAsync"/> returns only the owner's tasks of one agent, newest status timestamp first (ties by task ID, descending).
/// A page token is opaque to callers and stays valid while the task set changes.
/// </para>
/// <para>Package 05 replaces this contract text.</para>
/// </remarks>
public interface ITaskStore
{
    /// <summary>Returns a copy of the stored task, or null if it does not exist.</summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="taskId">The task ID.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The stored task, or <see langword="null"/> when it does not exist.</returns>
    ValueTask<StoredTask?> GetAsync(string agentName, string taskId, CancellationToken ct);

    /// <summary>Saves a copy of the task, replacing any existing task with the same agent and id.</summary>
    /// <param name="task">The task to save.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>A task that completes when the task is saved.</returns>
    ValueTask SaveAsync(StoredTask task, CancellationToken ct);

    /// <summary>
    /// Lists the owner's tasks for one agent, newest status timestamp first, one page at a time.
    /// Throws <see cref="A2AException"/> with <see cref="A2AErrorCode.InvalidParams"/> for a bad page size, history length or page token.
    /// </summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="owner">The owner key. Only this owner's tasks are listed.</param>
    /// <param name="request">The filters and the page.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>One page of tasks.</returns>
    /// <exception cref="A2AException">The page size, history length or page token is not valid (<see cref="A2AErrorCode.InvalidParams"/>).</exception>
    ValueTask<ListTasksResponse> ListAsync(string agentName, string owner, ListTasksRequest request, CancellationToken ct);
}
