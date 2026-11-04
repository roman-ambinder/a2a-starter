using Ambinder.A2A.Server.Stores;
using Microsoft.Extensions.Time.Testing;

namespace Ambinder.A2A.Testing;

/// <summary>A fresh, empty set of stores for one conformance test, and the clock the suite controls.</summary>
public sealed class StoreSet : IAsyncDisposable
{
    public required ITaskStore Tasks { get; init; }
    public required IPushConfigStore PushConfigs { get; init; }
    public required IOutboxStore Outbox { get; init; }
    public required IInstanceStore Instances { get; init; }
    public required FakeTimeProvider Time { get; init; }
    public StoreCapabilities Capabilities { get; init; } = new();

    /// <summary>Set when <see cref="StoreCapabilities.FaultInjection"/> is true.</summary>
    public IStoreFaultInjector? Faults { get; init; }

    /// <summary>Durable stores: reads the raw stored text of a push config, to prove credentials are protected at rest.</summary>
    public Func<TaskRef, string, CancellationToken, ValueTask<string?>>? ReadRawPushConfigAsync { get; init; }

    /// <summary>Disposes containers, connections or databases created for this set.</summary>
    public Func<ValueTask>? OnDispose { get; init; }

    public ValueTask DisposeAsync() => OnDispose?.Invoke() ?? ValueTask.CompletedTask;
}
