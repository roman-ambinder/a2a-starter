namespace Ambinder.A2A.Compat.V0_3.Shared.Models;

/// <summary>a2a.json <c>PushNotificationConfig</c>.</summary>
internal sealed record V03PushNotificationConfig
{
    public string? Id { get; init; }
    public string Url { get; init; } = "";
    public string? Token { get; init; }
    public V03PushNotificationAuthenticationInfo? Authentication { get; init; }
}

/// <summary>a2a.json <c>PushNotificationAuthenticationInfo</c>.</summary>
internal sealed record V03PushNotificationAuthenticationInfo
{
    public IReadOnlyList<string> Schemes { get; init; } = [];
    public string? Credentials { get; init; }
}

/// <summary>
/// a2a.json <c>TaskPushNotificationConfig</c> (<c>taskId</c> + <c>pushNotificationConfig</c>). The 0.3 proto names
/// the task by <see cref="Name"/> (<c>tasks/{id}/pushNotificationConfigs/{id}</c>) instead; both are read, only
/// the ones that are set are written.
/// </summary>
internal sealed record V03TaskPushNotificationConfig
{
    public string? TaskId { get; init; }
    public string? Name { get; init; }
    public V03PushNotificationConfig PushNotificationConfig { get; init; } = new();
}
