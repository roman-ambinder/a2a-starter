namespace Ambinder.A2A.Protocol;

/// <summary>Authentication details the agent uses when calling a webhook.</summary>
public sealed record AuthenticationInfo
{
    public string Scheme { get => field ?? ""; init; }
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
    public string? Tenant { get; init; }
    public string? Id { get; init; }
    public string? TaskId { get; init; }
    public string Url { get => field ?? ""; init; }
    public string? Token { get; init; }
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
