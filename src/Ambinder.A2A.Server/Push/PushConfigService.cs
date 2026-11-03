using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Push;

/// <summary>Validates and stores push configs for <see cref="Tasks.TaskManager"/>. Task existence and ownership are checked by the caller.</summary>
internal sealed class PushConfigService(IPushConfigStore store, PushOptions options)
{
    public async ValueTask<TaskPushNotificationConfig> SaveAsync(string agentName, string taskId, TaskPushNotificationConfig config, CancellationToken ct)
    {
        await ValidateAsync(config, ct).ConfigureAwait(false);
        return await store.SaveAsync(agentName, taskId, config, ct).ConfigureAwait(false);
    }

    public async ValueTask ValidateAsync(TaskPushNotificationConfig config, CancellationToken ct)
    {
        if (!Uri.TryCreate(config.Url, UriKind.Absolute, out var url))
            throw new A2AException(A2AErrorCode.InvalidParams, "The push notification config needs an absolute webhook url.");
        await WebhookUrlValidator.ValidateAsync(url, options.AllowPrivateNetworks, ct).ConfigureAwait(false);
    }

    public ValueTask<TaskPushNotificationConfig?> GetAsync(string agentName, string taskId, string id, CancellationToken ct)
        => store.GetAsync(agentName, taskId, id, ct);

    public ValueTask<ListTaskPushNotificationConfigsResponse> ListAsync(string agentName, string taskId, int? pageSize, string? pageToken, CancellationToken ct)
        => store.ListAsync(agentName, taskId, pageSize, pageToken, ct);

    public ValueTask DeleteAsync(string agentName, string taskId, string id, CancellationToken ct)
        => store.DeleteAsync(agentName, taskId, id, ct);
}
