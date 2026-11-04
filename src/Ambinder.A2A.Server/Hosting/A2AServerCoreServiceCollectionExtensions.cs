using Ambinder.A2A.Server;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Server.Auth;
using Ambinder.A2A.Server.Push;
using Ambinder.A2A.Server.Telemetry;
using Ambinder.A2A.Server.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the host-neutral A2A server core in a service collection.</summary>
public static class A2AServerCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the host-neutral A2A server core: options, agent catalog, in-memory task store, event broker,
    /// the traced <see cref="IA2ARequestHandler"/> (one span per call),
    /// push delivery (config store, signing key, sender) and <see cref="IOwnerResolver"/> (default <see cref="DefaultOwnerResolver"/>).
    /// Use this in a host without ASP.NET Core; web apps call <c>AddA2AServer</c> from <c>Ambinder.A2A.AspNetCore</c>, which calls this
    /// method.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the <see cref="A2AServerOptions"/>. At least <see cref="A2AServerOptions.PublicBaseUrl"/> is required. The options are checked when they are first used.</param>
    /// <returns>A builder to add agents to.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Lifetimes: everything is a singleton. The options are configured with <c>AddOptions</c>.
    /// These registrations use <c>TryAdd</c>, so a service you register before this call wins:
    /// <see cref="ITaskStore"/> (in-memory), <see cref="IPushConfigStore"/> (in-memory), <see cref="IOwnerResolver"/>,
    /// <see cref="IA2ARequestHandler"/> and the agent catalog. Observers registered as <see cref="ITaskEventObserver"/> are added
    /// (not replaced), and push delivery adds its sender as an observer and as a hosted service.
    /// The in-memory stores lose all data when the process stops. Use your own stores for anything that must survive a restart.
    /// </remarks>
    /// <example>
    /// <code>
    /// // A console or worker host without ASP.NET Core.
    /// var services = new ServiceCollection();
    /// services.AddLogging();
    /// services.AddA2AServerCore(o => o.PublicBaseUrl = "http://localhost:5200")
    ///     .AddAgent&lt;EchoAgent&gt;("echo", sp => new AgentCard { Name = "Echo", Description = "Repeats text.", Version = "1.0.0" });
    /// await using var provider = services.BuildServiceProvider();
    /// var handler = provider.GetRequiredService&lt;IA2ARequestHandler&gt;();
    /// </code>
    /// </example>
    public static A2AServerBuilder AddA2AServerCore(this IServiceCollection services, Action<A2AServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new A2AServerBuilder(services);

        services.AddOptions<A2AServerOptions>()
            .Configure(configure)
            .PostConfigure(o => o.Push.Issuer = o.BaseUrl);
        // Validation lives here, not in AddA2AServer, so it runs for hosts with and without ASP.NET Core.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<A2AServerOptions>, A2AServerOptionsValidator>());

        // One PushOptions instance for the task manager and the sender: the one inside A2AServerOptions.
        services.AddSingleton<IOptions<PushOptions>>(sp => Microsoft.Extensions.Options.Options.Create(sp.GetRequiredService<IOptions<A2AServerOptions>>().Value.Push));
        services.AddA2APush();

        services.TryAddSingleton<ITaskStore, InMemoryTaskStore>();
        services.TryAddSingleton<TaskEventBroker>(_ => new TaskEventBroker());
        services.TryAddSingleton<AgentCatalog>(builder.BuildCatalog);
        services.TryAddSingleton<TaskManager>(sp => new TaskManager(
            sp.GetRequiredService<AgentCatalog>(),
            sp.GetRequiredService<ITaskStore>(),
            sp.GetRequiredService<TaskEventBroker>(),
            sp,
            sp.GetServices<ITaskEventObserver>(),
            sp.GetService<ILogger<TaskManager>>(),
            sp.GetService<TimeProvider>(),
            sp.GetRequiredService<IPushConfigStore>(),
            sp.GetRequiredService<IOptions<PushOptions>>().Value,
            sp.GetRequiredService<IOwnerResolver>()));
        services.TryAddSingleton<IA2ARequestHandler>(sp => new TracingRequestHandler(sp.GetRequiredService<TaskManager>()));

        services.TryAddSingleton<IOwnerResolver>(DefaultOwnerResolver.Instance);
        return builder;
    }
}
