using Ambinder.A2A.Server.Stores;
using Microsoft.Extensions.Time.Testing;

namespace Ambinder.A2A.Testing;

/// <summary>A fresh, empty set of stores for one conformance test, and the clock the suite controls.</summary>
public sealed class StoreSet : IAsyncDisposable
{
    /// <summary>The task store under test.</summary>
    public required ITaskStore Tasks { get; init; }
    /// <summary>The push-notification config store under test.</summary>
    public required IPushConfigStore PushConfigs { get; init; }
    /// <summary>The outbox store under test.</summary>
    public required IOutboxStore Outbox { get; init; }
    /// <summary>The instance-registry store under test.</summary>
    public required IInstanceStore Instances { get; init; }
    /// <summary>The clock the stores were built with.</summary>
    /// <remarks>The conformance suite advances this clock to drive expiry, leases and timestamps; stores must read time only from it.</remarks>
    public required FakeTimeProvider Time { get; init; }
    /// <summary>What the store implementation supports; the suite skips tests for unsupported capabilities. Defaults to none.</summary>
    public StoreCapabilities Capabilities { get; init; } = new();

    /// <summary>Set when <see cref="StoreCapabilities.FaultInjection"/> is true.</summary>
    public IStoreFaultInjector? Faults { get; init; }

    /// <summary>Durable stores: reads the raw stored text of a push config, to prove credentials are protected at rest.</summary>
    public Func<TaskRef, string, CancellationToken, ValueTask<string?>>? ReadRawPushConfigAsync { get; init; }

    /// <summary>Disposes containers, connections or databases created for this set.</summary>
    /// <remarks>Runs at most once, on the first <see cref="DisposeAsync"/> call. If it throws, the exception propagates from that first call only.</remarks>
    public Func<ValueTask>? OnDispose { get; init; }

    private int _disposed;

    /// <summary>
    /// Runs <see cref="OnDispose"/> on the first call only. Idempotent: later calls do nothing,
    /// including after <see cref="OnDispose"/> threw (the exception is not rethrown or retried).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (OnDispose is { } onDispose)
        {
            await onDispose().ConfigureAwait(false);
        }
    }
}
