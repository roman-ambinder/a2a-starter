using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WebhookReceiver;

/// <summary>What a client told the receiver to expect for one task.</summary>
/// <remarks><see cref="AnyTask"/> (<c>"*"</c>) as task id accepts any task: a client cannot know the task id before its first send.</remarks>
public sealed record Subscription(string TaskId, string Token, string Credentials)
{
    public const string AnyTask = "*";
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

    /// <summary>Stores the payload unless this task already has one with the same body hash. Returns false for a duplicate.</summary>
    public bool TryAdd(string taskId, string bodyHash, JsonElement payload)
    {
        lock (_gate)
        {
            if (!_notifications.TryGetValue(taskId, out var list)) _notifications[taskId] = list = [];
            if (list.Any(n => n.Hash == bodyHash)) return false;
            list.Add((bodyHash, payload.Clone()));
            return true;
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
