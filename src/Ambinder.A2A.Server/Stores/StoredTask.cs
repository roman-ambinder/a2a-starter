using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Security;

namespace Ambinder.A2A.Server.Stores;

/// <summary>A task with everything the server stores about it.</summary>
public sealed record StoredTask
{
    /// <summary>The task key: tenant, agent and task id.</summary>
    public required TaskKey Key { get; init; }

    /// <summary>String form of package 07's <c>OwnerKey</c>. Frozen format (B7). Opaque to stores.</summary>
    public required string Owner { get; init; }

    /// <summary>The protocol task. <c>Status.Timestamp</c> is always set by the core.</summary>
    public required AgentTask Task { get; init; }

    /// <summary>Opaque concurrency token. Null only before create.</summary>
    public string? Version { get; init; }

    /// <summary>+1 per committed event. Orders events and outbox rows.</summary>
    public long Sequence { get; init; }

    /// <summary>When the task was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the task was last written.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Set by the core from retention options. Null means never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>The instance running the task now; null when no execution is live.</summary>
    public TaskRunner? Runner { get; init; }

    /// <summary>Durable cancel signal (06, 09).</summary>
    public DateTimeOffset? CancelRequestedAt { get; init; }

    /// <summary>Recovery restarts (09).</summary>
    public int RunAttempt { get; init; }

    /// <summary>The <c>messageId</c> of the message that created the task (dedupe).</summary>
    public string? OriginMessageId { get; init; }

    /// <summary>Package 07's grant. Set at AUTH_REQUIRED, cleared on leaving it.</summary>
    public TaskContinuationGrant? ContinuationGrant { get; init; }

    /// <summary>Kept when <see cref="Runner"/> is cleared. Continuation affinity (06).</summary>
    public string? LastRunnerInstanceId { get; init; }

    /// <summary>Reduced caller for runs on other pods and recovery (07, B8). Never holds a token.</summary>
    public StoredCaller? Caller { get; init; }
}

/// <summary>The instance running a task and when it claimed it.</summary>
public sealed record TaskRunner
{
    /// <summary>The id of the instance running the task.</summary>
    public required string InstanceId { get; init; }

    /// <summary>When the instance claimed the task.</summary>
    public required DateTimeOffset ClaimedAt { get; init; }
}

/// <summary>Reduced caller, persisted with the task (B8). Never holds an access token.</summary>
public sealed record StoredCaller
{
    /// <summary>The caller's owner key string (07).</summary>
    public required string Owner { get; init; }

    /// <summary>The Entra tenant id of the caller, if known.</summary>
    public string? EntraTenantId { get; init; }

    /// <summary>The application id of the caller, if known.</summary>
    public string? AppId { get; init; }

    /// <summary>The user object id of the caller, if known.</summary>
    public string? UserObjectId { get; init; }

    /// <summary>The caller's roles. Empty when none.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>The caller's scopes. Empty when none.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];
}
