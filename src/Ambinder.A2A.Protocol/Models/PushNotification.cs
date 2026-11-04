namespace Ambinder.A2A.Protocol;

/// <summary>Authentication details the agent uses when calling a webhook.</summary>
public sealed record AuthenticationInfo
{
    /// <summary>The authentication scheme the agent uses for the webhook, for example <c>Bearer</c>.</summary>
    public string Scheme { get => field ?? ""; init; }
    /// <summary>The credentials for the scheme. This value is a secret: <see cref="object.ToString"/> never shows it.</summary>
    public string? Credentials { get; init; }

    /// <summary>Writes the members for <see cref="object.ToString"/>. <see cref="Credentials"/> is never written.</summary>
    /// <param name="builder">The builder to write to.</param>
    /// <returns><see langword="true"/> when members were written.</returns>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Scheme = ").Append(Scheme).Append(", Credentials = ").Append(Credentials is null ? "null" : "***");
        return true;
    }
}

/// <summary>A push notification (webhook) configuration for a task.</summary>
public sealed record TaskPushNotificationConfig
{
    /// <summary>The tenant of the task, or <see langword="null"/>.</summary>
    public string? Tenant { get; init; }
    /// <summary>The ID of this configuration. The server creates one when it is <see langword="null"/>.</summary>
    public string? Id { get; init; }
    /// <summary>The ID of the task the configuration belongs to.</summary>
    public string? TaskId { get; init; }
    /// <summary>The absolute URL of the webhook that receives the notifications.</summary>
    public string Url { get => field ?? ""; init; }
    /// <summary>A token the agent sends back in the <c>X-A2A-Notification-Token</c> header. This value is a secret: <see cref="object.ToString"/> never shows it.</summary>
    public string? Token { get; init; }
    /// <summary>How the agent authenticates to the webhook, or <see langword="null"/>.</summary>
    public AuthenticationInfo? Authentication { get; init; }

    /// <summary>Writes the members for <see cref="object.ToString"/>. <see cref="Token"/> is never written, and
    /// <see cref="Authentication"/> prints without its credentials.</summary>
    /// <param name="builder">The builder to write to.</param>
    /// <returns><see langword="true"/> when members were written.</returns>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        // The URL is printed as is. If a URL can carry a secret in its query, that is package 08's call (push security).
        builder.Append("Tenant = ").Append(Tenant)
            .Append(", Id = ").Append(Id)
            .Append(", TaskId = ").Append(TaskId)
            .Append(", Url = ").Append(Url)
            .Append(", Token = ").Append(Token is null ? "null" : "***")
            .Append(", Authentication = ").Append(Authentication?.ToString() ?? "null");
        return true;
    }
}
