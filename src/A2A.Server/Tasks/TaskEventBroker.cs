using System.Runtime.CompilerServices;
using System.Threading.Channels;
using A2A.Protocol;

namespace A2A.Server.Tasks;

/// <summary>
/// In-process fan-out of task events to live subscribers (streaming send, SubscribeToTask).
/// Each subscriber has its own bounded channel. <see cref="Publish"/> never waits: a subscriber whose
/// channel is full is dropped (its stream ends with an <see cref="A2AException"/>), so a slow reader
/// cannot block the agent. All subscribers of a task are completed after a final event
/// (a message, or a terminal or interrupted state).
/// </summary>
public sealed class TaskEventBroker
{
    public const int DefaultCapacity = 256;

    private readonly int _capacity;
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Agent, string TaskId), List<Subscription>> _subscribers = new();

    public TaskEventBroker(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>Delivers an event to the task's current subscribers. Does nothing when there are none.</summary>
    public void Publish(string agent, string taskId, StreamResponse evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var key = (agent, taskId);
        var final = TaskStateRules.IsFinal(evt);
        Subscription[] targets;
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(key, out var list)) return;
            targets = [.. list];
            if (final) _subscribers.Remove(key);
        }

        foreach (var sub in targets)
        {
            if (!sub.Channel.Writer.TryWrite(evt))
            {
                Drop(sub, new A2AException(A2AErrorCode.InternalError, "The subscriber fell behind and was disconnected."));
                continue;
            }
            if (final) Drop(sub, null);
        }
    }

    /// <summary>
    /// Registers a subscriber now (not on first enumeration), so no event published after this call is missed.
    /// The subscriber is removed when <paramref name="ct"/> is cancelled, when the enumeration ends or is
    /// disposed, or after a final event. Enumerate the result once.
    /// </summary>
    public IAsyncEnumerable<StreamResponse> Subscribe(string agent, string taskId, CancellationToken ct)
        => ReadAndDisposeAsync(Open(agent, taskId, ct), ct);

    /// <summary>Like <see cref="Subscribe"/>, but the caller owns the subscription and must dispose it.</summary>
    internal Subscription Open(string agent, string taskId, CancellationToken ct)
    {
        var sub = new Subscription(this, (agent, taskId), Channel.CreateBounded<StreamResponse>(new BoundedChannelOptions(_capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait, // TryWrite fails when full; Publish then drops the subscriber.
        }));
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(sub.Key, out var list)) _subscribers[sub.Key] = list = [];
            list.Add(sub);
        }
        sub.Registration = ct.Register(sub.Dispose);
        return sub;
    }

    internal int SubscriberCount(string agent, string taskId)
    {
        lock (_gate) return _subscribers.TryGetValue((agent, taskId), out var list) ? list.Count : 0;
    }

    private static async IAsyncEnumerable<StreamResponse> ReadAndDisposeAsync(Subscription sub, [EnumeratorCancellation] CancellationToken ct)
    {
        using (sub)
        {
            await foreach (var evt in sub.ReadAllAsync(ct).ConfigureAwait(false))
                yield return evt;
        }
    }

    private void Drop(Subscription sub, Exception? error)
    {
        lock (_gate)
        {
            if (_subscribers.TryGetValue(sub.Key, out var list) && list.Remove(sub) && list.Count == 0)
                _subscribers.Remove(sub.Key);
        }
        sub.Registration.Unregister();
        sub.Channel.Writer.TryComplete(error);
    }

    /// <summary>One subscriber's channel. Dispose to unsubscribe.</summary>
    internal sealed class Subscription(TaskEventBroker broker, (string Agent, string TaskId) key, Channel<StreamResponse> channel) : IDisposable
    {
        public (string Agent, string TaskId) Key { get; } = key;
        public Channel<StreamResponse> Channel { get; } = channel;
        public CancellationTokenRegistration Registration { get; set; }

        /// <summary>Yields events until the stream completes. Throws <see cref="OperationCanceledException"/> when cancelled.</summary>
        public async IAsyncEnumerable<StreamResponse> ReadAllAsync([EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var evt in Channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return evt;
            ct.ThrowIfCancellationRequested(); // cancellation may complete the channel before the read observes it
        }

        public void Dispose() => broker.Drop(this, null);
    }
}
