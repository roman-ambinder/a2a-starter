using Ambinder.A2A.Server.Stores;
using Ambinder.A2A.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Ambinder.A2A.TestHarness.Tests;

public class StoreSetTests
{
    // The stores are irrelevant to these tests, so the null stubs live in exactly one place.
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
    public async Task DisposeAsync_invokes_OnDispose()
    {
        var calls = 0;
        var set = Create(() => { calls++; return ValueTask.CompletedTask; });

        await set.DisposeAsync();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DisposeAsync_twice_invokes_OnDispose_exactly_once()
    {
        var calls = 0;
        var set = Create(() => { calls++; return ValueTask.CompletedTask; });

        await set.DisposeAsync();
        await set.DisposeAsync();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DisposeAsync_awaits_a_genuinely_async_OnDispose()
    {
        var finished = false;
        var set = Create(async () =>
        {
            await Task.Yield();
            finished = true;
        });

        await set.DisposeAsync();

        Assert.True(finished);
    }

    [Fact]
    public async Task DisposeAsync_propagates_a_throwing_OnDispose_once_and_never_retries()
    {
        var calls = 0;
        var set = Create(() =>
        {
            calls++;
            throw new InvalidOperationException("boom");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await set.DisposeAsync());
        await set.DisposeAsync();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DisposeAsync_without_OnDispose_completes()
    {
        await Create().DisposeAsync();
    }

    [Fact]
    public void Capabilities_default_to_the_empty_set()
    {
        Assert.Equal(new StoreCapabilities(), Create().Capabilities);
    }

    [Fact]
    public void Capabilities_default_is_not_durable()
    {
        Assert.False(Create().Capabilities.Durable);
    }

    [Fact]
    public void Capabilities_default_has_no_fault_injection()
    {
        Assert.False(Create().Capabilities.FaultInjection);
    }

    [Fact]
    public void Faults_defaults_to_null()
    {
        Assert.Null(Create().Faults);
    }

    [Fact]
    public void ReadRawPushConfigAsync_defaults_to_null()
    {
        Assert.Null(Create().ReadRawPushConfigAsync);
    }

    [Fact]
    public void OnDispose_defaults_to_null()
    {
        Assert.Null(Create().OnDispose);
    }
}
