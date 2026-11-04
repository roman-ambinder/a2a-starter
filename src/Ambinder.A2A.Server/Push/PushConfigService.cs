using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Push;

/// <summary>Validates and stores push configs for the task manager. Task existence and ownership are checked by the caller.</summary>
internal sealed class PushConfigService(IPushConfigStore store, PushOptions options)
{
    private const int MaxSchemeLength = 64;

    private readonly PushAddressPolicy _policy = new(options);

    public async ValueTask<TaskPushNotificationConfig> SaveAsync(string agentName, string taskId, TaskPushNotificationConfig config, CancellationToken ct)
    {
        await ValidateAsync(config, ct).ConfigureAwait(false);
        return await store.SaveAsync(agentName, taskId, config, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks the webhook URL (SSRF guard), the authentication scheme and credentials, and the token. Messages never
    /// contain the credentials or the token.
    /// </summary>
    public async ValueTask ValidateAsync(TaskPushNotificationConfig config, CancellationToken ct)
    {
        if (!Uri.TryCreate(config.Url, UriKind.Absolute, out var url))
            throw new A2AException(A2AErrorCode.InvalidParams, "The push notification config needs an absolute webhook url. Send a full URL such as https://host/path.");
        await WebhookUrlValidator.ValidateAsync(url, _policy, ct).ConfigureAwait(false);

        if (config.Authentication is { } authentication)
        {
            if (!IsToken(authentication.Scheme))
                throw new A2AException(A2AErrorCode.InvalidParams, "The push authentication scheme must be an HTTP token of 1 to 64 characters, for example Bearer.");
            if (HasControlCharacters(authentication.Credentials))
                throw new A2AException(A2AErrorCode.InvalidParams, "The push authentication credentials must not contain control characters such as CR or LF.");
        }
        if (HasControlCharacters(config.Token))
            throw new A2AException(A2AErrorCode.InvalidParams, "The push notification token must not contain control characters such as CR or LF.");
    }

    public ValueTask<TaskPushNotificationConfig?> GetAsync(string agentName, string taskId, string id, CancellationToken ct)
        => store.GetAsync(agentName, taskId, id, ct);

    public ValueTask<ListTaskPushNotificationConfigsResponse> ListAsync(string agentName, string taskId, int? pageSize, string? pageToken, CancellationToken ct)
        => store.ListAsync(agentName, taskId, pageSize, pageToken, ct);

    public ValueTask DeleteAsync(string agentName, string taskId, string id, CancellationToken ct)
        => store.DeleteAsync(agentName, taskId, id, ct);

    /// <summary>RFC 9110 token: 1 to 64 <c>tchar</c> characters.</summary>
    private static bool IsToken(string value)
    {
        if (value.Length is 0 or > MaxSchemeLength) return false;
        foreach (var c in value)
        {
            var ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
                || "!#$%&'*+-.^_`|~".Contains(c, StringComparison.Ordinal);
            if (!ok) return false;
        }
        return true;
    }

    private static bool HasControlCharacters(string? value)
        => value is not null && value.Any(c => c < 0x20 || c == 0x7f);
}
