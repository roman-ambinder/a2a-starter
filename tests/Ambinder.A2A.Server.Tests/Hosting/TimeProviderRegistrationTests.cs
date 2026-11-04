using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.Server.Tests.Hosting;

/// <summary>Spec Goal 12 and 7.12: all server code reads time from the DI TimeProvider, registered by AddA2AServerCore.</summary>
public class TimeProviderRegistrationTests
{
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    [Fact]
    public void AddA2AServerCore_registers_the_system_clock_by_default()
    {
        var services = Services();
        services.AddA2AServerCore(o => o.PublicBaseUrl = "http://localhost:5200");
        using var provider = services.BuildServiceProvider();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void A_TimeProvider_registered_before_AddA2AServerCore_wins()
    {
        var fake = new FixedTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var services = Services();
        services.AddSingleton<TimeProvider>(fake);
        services.AddA2AServerCore(o => o.PublicBaseUrl = "http://localhost:5200");
        using var provider = services.BuildServiceProvider();

        Assert.Same(fake, provider.GetRequiredService<TimeProvider>());
    }
}
