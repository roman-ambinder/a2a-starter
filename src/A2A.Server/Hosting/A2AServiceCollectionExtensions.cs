using A2A.Server.Agents;
using A2A.Server.Auth;
using A2A.Server.Push;
using A2A.Server.Telemetry;
using A2A.Server.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace A2A.Server.Hosting;

public static class A2AServiceCollectionExtensions
{
    /// <summary>
    /// Registers the A2A server: options, agent catalog, in-memory task store, event broker, <see cref="TaskManager"/>
    /// wrapped in <see cref="TracingRequestHandler"/> as <see cref="IA2ARequestHandler"/>, push delivery (config store, signing key, sender) gRPC services, <see cref="IAgentAuthorizer"/>,
    /// <see cref="IOwnerResolver"/> (default <see cref="DefaultOwnerResolver"/>) and a startup check
    /// (unknown policy, default agent or gRPC port key → <see cref="InvalidOperationException"/> before the server listens).
    /// Authentication schemes are the app's job; call <c>UseAuthentication()</c> before <c>MapA2A()</c>.
    /// </summary>
    public static A2AServerBuilder AddA2AServer(this IServiceCollection services, Action<A2AServerOptions> configure)
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

        services.AddGrpc();
        services.AddAuthorization();
        services.TryAddSingleton<IAgentAuthorizer, AgentAuthorizer>();
        services.TryAddSingleton<IOwnerResolver>(DefaultOwnerResolver.Instance);
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, A2AStartupValidation>());
        return builder;
    }
}
