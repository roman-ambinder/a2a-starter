using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Auth;
using Ambinder.A2A.Server.Push;
using Ambinder.A2A.Server.Telemetry;
using Ambinder.A2A.Server.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Server.Hosting;

public static class A2AServiceCollectionExtensions
{
    /// <summary>
    /// Registers the host-neutral A2A server core: options, agent catalog, in-memory task store, event broker,
    /// the traced <see cref="IA2ARequestHandler"/> (one span per call),
    /// push delivery (config store, signing key, sender) and <see cref="IOwnerResolver"/> (default <see cref="DefaultOwnerResolver"/>).
    /// A web app calls <c>AddA2AServer</c> from <c>Ambinder.A2A.AspNetCore</c> instead, which calls this and adds the
    /// ASP.NET Core parts.
    /// </summary>
    public static A2AServerBuilder AddA2AServerCore(this IServiceCollection services, Action<A2AServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new A2AServerBuilder(services);

        services.AddOptions<A2AServerOptions>()
            .Configure(configure)
            .Validate(o => Uri.TryCreate(o.PublicBaseUrl, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps),
                "A2AServerOptions.PublicBaseUrl must be an absolute http(s) URL.")
            .Validate(o => o.MaxRequestBodyBytes > 0, "A2AServerOptions.MaxRequestBodyBytes must be positive.")
            .PostConfigure(o => o.Push.Issuer = o.BaseUrl);

        // One PushOptions instance for the task manager and the sender: the one inside A2AServerOptions.
        services.AddSingleton<IOptions<PushOptions>>(sp => Options.Create(sp.GetRequiredService<IOptions<A2AServerOptions>>().Value.Push));
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
