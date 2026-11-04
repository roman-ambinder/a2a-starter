using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Push;

/// <summary>Stores push notification configs, keyed by agent name, task id and config id.</summary>
/// <remarks>
/// The default store is registered as a singleton and keeps configs in memory; the default is added with <c>TryAdd</c>, so register your
/// own before <c>AddA2AServer</c> to replace it. Calls can overlap, including calls for the same task, so an implementation must be
/// thread-safe. The server does not hold a lock while it calls the store. Configs are listed in creation order.
/// </remarks>
public interface IPushConfigStore
{
    /// <summary>Saves the config (replacing one with the same id). Assigns a new GUID id when <c>Id</c> is empty. Returns what was stored.</summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="taskId">The task ID.</param>
    /// <param name="config">The config to save.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The stored config, with its ID.</returns>
    ValueTask<TaskPushNotificationConfig> SaveAsync(string agentName, string taskId, TaskPushNotificationConfig config, CancellationToken ct);

    /// <summary>Gets one config.</summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="taskId">The task ID.</param>
    /// <param name="configId">The config ID.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The config, or <see langword="null"/> when it does not exist.</returns>
    ValueTask<TaskPushNotificationConfig?> GetAsync(string agentName, string taskId, string configId, CancellationToken ct);

    /// <summary>Lists configs in creation order, one page at a time. Throws InvalidParams for a bad page size or token.</summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="taskId">The task ID.</param>
    /// <param name="pageSize">The most configs to return, or <see langword="null"/> for the default.</param>
    /// <param name="pageToken">The token of the page to read, or <see langword="null"/> for the first page.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>One page of configs.</returns>
    /// <exception cref="Protocol.A2AException">The page size or the token is not valid (<see cref="Protocol.A2AErrorCode.InvalidParams"/>).</exception>
    ValueTask<ListTaskPushNotificationConfigsResponse> ListAsync(string agentName, string taskId, int? pageSize, string? pageToken, CancellationToken ct);

    /// <summary>All configs of a task, for delivery.</summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="taskId">The task ID.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>Every config of the task, in creation order. Empty when there are none.</returns>
    ValueTask<IReadOnlyList<TaskPushNotificationConfig>> GetAllAsync(string agentName, string taskId, CancellationToken ct);

    /// <summary>Removes one config. Succeeds when it does not exist.</summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="taskId">The task ID.</param>
    /// <param name="configId">The config ID.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>A task that completes when the config is gone.</returns>
    ValueTask DeleteAsync(string agentName, string taskId, string configId, CancellationToken ct);

    /// <summary>Removes every config of a task.</summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="taskId">The task ID.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>A task that completes when the configs are gone.</returns>
    ValueTask DeleteAllAsync(string agentName, string taskId, CancellationToken ct);
}
