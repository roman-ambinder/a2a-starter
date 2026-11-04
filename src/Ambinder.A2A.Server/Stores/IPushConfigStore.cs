using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Stores;

/// <summary>A push config as stored, with its creation order and metadata.</summary>
public sealed record StoredPushConfig
{
    /// <summary>The push notification config as the client sent it (credentials unprotected).</summary>
    public required TaskPushNotificationConfig Config { get; init; }

    /// <summary>The task sequence at creation. Gives creation order.</summary>
    public required long CreatedSequence { get; init; }

    /// <summary>When the config was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Opaque protocol version of the API that created the config (<c>1.0</c>, or <c>0.3</c> from package 15). Null means <c>1.0</c>. Package 08 picks the payload formatter by it.</summary>
    public string? ProtocolVersion { get; init; }

    /// <summary>Opaque push signature issuer: the public base URL of the request that created the config, set only when the server has no <c>A2A:PublicBaseUrl</c>. Null means use the configured issuer. Package 08 signs with it.</summary>
    public string? Issuer { get; init; }
}

/// <summary>One page of push configs.</summary>
public sealed record PushConfigPage
{
    /// <summary>The configs on this page, in creation order.</summary>
    public required IReadOnlyList<StoredPushConfig> Configs { get; init; }

    /// <summary>The <c>CreatedSequence</c> to pass as <c>afterSequence</c> for the next page; null on the last page.</summary>
    public long? NextAfter { get; init; }
}

/// <summary>Stores push notification configs, per task.</summary>
/// <remarks>
/// Guarantees an implementer must give:
/// <list type="bullet">
/// <item><description>Thread-safe.</description></item>
/// <item><description><see cref="UpsertAsync"/> inserts or replaces by (task, config id) and returns what was stored. The core assigns the id when empty. It stores <c>protocolVersion</c> and <c>issuer</c> unchanged.</description></item>
/// <item><description><see cref="ListAsync"/> and <see cref="GetAllAsync"/> return configs in creation order (<c>CreatedSequence</c>). <c>afterSequence</c> is exclusive. <c>NextAfter</c> is null on the last page.</description></item>
/// <item><description><see cref="DeleteAsync"/> is idempotent: deleting a missing config is not an error.</description></item>
/// <item><description>Tenant comes from <see cref="TaskRef.Key"/> and is part of every key. Never return a config of another tenant.</description></item>
/// <item><description>Configs are deleted with their task (<see cref="ITaskStore.DeleteAsync"/> and purge) and expire with it.</description></item>
/// <item><description>Durable stores must protect <c>authentication.credentials</c> and <c>token</c> at rest with the registered push credential protector (<c>Protect</c> before every write, <c>Unprotect</c> after every read). The in-memory store need not.</description></item>
/// <item><description>No protocol validation. Throw only <see cref="A2AStoreException"/> and <see cref="OperationCanceledException"/>.</description></item>
/// </list>
/// </remarks>
public interface IPushConfigStore
{
    /// <summary>Inserts or replaces a config by (task, config id).</summary>
    /// <param name="task">The task, with its owner.</param>
    /// <param name="config">The config. The core has assigned the id.</param>
    /// <param name="protocolVersion">The protocol version of the creating API; null means <c>1.0</c>. Stored unchanged.</param>
    /// <param name="issuer">The push signature issuer; stored unchanged.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>What was stored.</returns>
    ValueTask<StoredPushConfig> UpsertAsync(TaskRef task, TaskPushNotificationConfig config, string? protocolVersion, string? issuer, CancellationToken ct);

    /// <summary>Gets one config.</summary>
    /// <param name="task">The task, with its owner.</param>
    /// <param name="configId">The config id.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The config, or null when it does not exist.</returns>
    ValueTask<StoredPushConfig?> GetAsync(TaskRef task, string configId, CancellationToken ct);

    /// <summary>Lists configs of a task in creation order.</summary>
    /// <param name="task">The task, with its owner.</param>
    /// <param name="pageSize">The most configs to return.</param>
    /// <param name="afterSequence">Return configs created after this sequence (exclusive), or null for the first page.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>One page.</returns>
    ValueTask<PushConfigPage> ListAsync(TaskRef task, int pageSize, long? afterSequence, CancellationToken ct);

    /// <summary>Gets all configs of a task, for delivery.</summary>
    /// <param name="task">The task, with its owner.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>All configs in creation order.</returns>
    ValueTask<IReadOnlyList<StoredPushConfig>> GetAllAsync(TaskRef task, CancellationToken ct);

    /// <summary>Counts the configs of a task.</summary>
    /// <param name="task">The task, with its owner.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The count.</returns>
    ValueTask<int> CountAsync(TaskRef task, CancellationToken ct);

    /// <summary>Deletes one config. Idempotent.</summary>
    /// <param name="task">The task, with its owner.</param>
    /// <param name="configId">The config id.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>A task that completes when the call is done.</returns>
    ValueTask DeleteAsync(TaskRef task, string configId, CancellationToken ct);

    /// <summary>Deletes all configs of a task.</summary>
    /// <param name="task">The task, with its owner.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>A task that completes when the call is done.</returns>
    ValueTask DeleteAllAsync(TaskRef task, CancellationToken ct);
}
