namespace A2A.Protocol;

/// <summary>Authentication details the agent uses when calling a webhook.</summary>
public sealed record AuthenticationInfo
{
    public string Scheme { get => field ?? ""; init; }
    public string? Credentials { get; init; }
}

/// <summary>A push notification (webhook) configuration for a task.</summary>
public sealed record TaskPushNotificationConfig
{
    public string? Tenant { get; init; }
    public string? Id { get; init; }
    public string? TaskId { get; init; }
    public string Url { get => field ?? ""; init; }
    public string? Token { get; init; }
    public AuthenticationInfo? Authentication { get; init; }
}
