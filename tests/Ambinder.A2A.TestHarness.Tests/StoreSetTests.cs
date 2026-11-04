using Ambinder.A2A.Server.Stores;
using Ambinder.A2A.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Ambinder.A2A.TestHarness.Tests;

public class StoreSetTests
{
    private static StoreSet Create(Func<ValueTask>? onDispose = null) => new()
    {
        Tasks = null!,
        PushConfigs = null!,
        Outbox = null!,
        Instances = null!,
        Time = new FakeTimeProvider(),
        OnDispose = onDispose,
    };

    [Fact]
    public async Task DisposeAsync_invokes_OnDispose_once_per_call()
    {
        var calls = 0;
        var set = Create(() => { calls++; return ValueTask.CompletedTask; });

        await set.DisposeAsync();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DisposeAsync_without_OnDispose_completes()
    {
        await Create().DisposeAsync();
    }

    [Fact]
    public void Defaults_are_empty_capabilities_and_no_fault_injector_or_raw_reader()
    {
        var set = Create();

        Assert.NotNull(set.Capabilities);
        Assert.Equal(new StoreCapabilities(), set.Capabilities);
        Assert.Null(set.Faults);
        Assert.Null(set.ReadRawPushConfigAsync);
        Assert.Null(set.OnDispose);
    }
}
