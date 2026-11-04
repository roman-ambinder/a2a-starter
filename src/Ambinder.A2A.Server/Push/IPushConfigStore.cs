using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Push;

/// <summary>Stores push notification configs, keyed by agent name, task id and config id.</summary>
public interface IPushConfigStore
{
    /// <summary>Saves the config (replacing one with the same id). Assigns a new GUID id when <c>Id</c> is empty. Returns what was stored.</summary>
    ValueTask<TaskPushNotificationConfig> SaveAsync(string agentName, string taskId, TaskPushNotificationConfig config, CancellationToken ct);

    ValueTask<TaskPushNotificationConfig?> GetAsync(string agentName, string taskId, string configId, CancellationToken ct);

    /// <summary>Lists configs in creation order, one page at a time. Throws InvalidParams for a bad page size or token.</summary>
    ValueTask<ListTaskPushNotificationConfigsResponse> ListAsync(string agentName, string taskId, int? pageSize, string? pageToken, CancellationToken ct);

    /// <summary>All configs of a task, for delivery.</summary>
    ValueTask<IReadOnlyList<TaskPushNotificationConfig>> GetAllAsync(string agentName, string taskId, CancellationToken ct);

    /// <summary>Removes one config. Succeeds when it does not exist.</summary>
    ValueTask DeleteAsync(string agentName, string taskId, string configId, CancellationToken ct);

    /// <summary>Removes every config of a task.</summary>
    ValueTask DeleteAllAsync(string agentName, string taskId, CancellationToken ct);
}
