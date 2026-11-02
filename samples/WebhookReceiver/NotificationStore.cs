using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WebhookReceiver;

/// <summary>What a client told the receiver to expect for one task.</summary>
/// <remarks>
/// <see cref="AnyTask"/> (<c>"*"</c>) as task id means "not bound yet": the first accepted push binds the subscription to that
/// push's task (<see cref="BoundTaskId"/>); later pushes for another task with the same credentials are rejected.
/// </remarks>
public sealed class Subscription(string taskId, string token, string credentials)
{
    public const string AnyTask = "*";
    public string TaskId { get; } = taskId;
    public string Token { get; } = token;
    public string Credentials { get; } = credentials;
    /// <summary>Set once by <see cref="NotificationStore.TryAccept"/>, under the store lock.</summary>
    public string? BoundTaskId { get; internal set; }
}

/// <summary>In-memory subscriptions and received notifications. Notifications are keyed by task id and kept in arrival order.</summary>
public sealed class NotificationStore
{
    private readonly object _gate = new();
    private readonly List<Subscription> _subscriptions = [];
    private readonly Dictionary<string, List<(string Hash, JsonElement Payload)>> _notifications = new(StringComparer.Ordinal);

    public void AddSubscription(Subscription subscription)
    {
        lock (_gate) _subscriptions.Add(subscription);
    }

    /// <summary>
    /// The subscriptions whose credentials AND token both equal the given values (fixed-time comparison, so response time
    /// does not leak how much matched). Null or missing values never match.
    /// </summary>
    public IReadOnlyList<Subscription> Match(string? credentials, string? token)
    {
        if (string.IsNullOrEmpty(credentials) || string.IsNullOrEmpty(token)) return [];
        Subscription[] snapshot;
        lock (_gate) snapshot = [.. _subscriptions];
        return snapshot.Where(s => FixedEquals(s.Credentials, credentials) & FixedEquals(s.Token, token)).ToList();
    }

    public enum AcceptResult { Rejected, Stored, Duplicate }

    /// <summary>
    /// Atomically: finds a matching subscription for <paramref name="taskId"/> (exact, or a wildcard that is unbound or already bound
    /// to this task), binds a wildcard to the task, and stores the payload unless this task already has one with the same body hash.
    /// </summary>
    public AcceptResult TryAccept(IReadOnlyList<Subscription> matches, string taskId, string bodyHash, JsonElement payload)
    {
        lock (_gate)
        {
            var exact = matches.Any(m => m.TaskId == taskId);
            var wildcard = matches.FirstOrDefault(m => m.TaskId == Subscription.AnyTask && (m.BoundTaskId is null || m.BoundTaskId == taskId));
            if (!exact && wildcard is null) return AcceptResult.Rejected;
            if (wildcard is not null) wildcard.BoundTaskId = taskId;
            if (!_notifications.TryGetValue(taskId, out var list)) _notifications[taskId] = list = [];
            if (list.Any(n => n.Hash == bodyHash)) return AcceptResult.Duplicate;
            list.Add((bodyHash, payload.Clone()));
            return AcceptResult.Stored;
        }
    }

    public IReadOnlyList<JsonElement> Get(string taskId)
    {
        lock (_gate)
            return _notifications.TryGetValue(taskId, out var list) ? list.Select(n => n.Payload).ToList() : [];
    }

    /// <summary>Equal-length comparison via SHA-256 of both sides, then <see cref="CryptographicOperations.FixedTimeEquals"/>.</summary>
    public static bool FixedEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}
