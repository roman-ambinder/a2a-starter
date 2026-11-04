using Ambinder.A2A.AspNetCore.Hosting;
using Ambinder.A2A.AspNetCore.Auth;
using Ambinder.A2A.Server.Auth;
using Ambinder.A2A.Server.Security;
using Ambinder.A2A.Server.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the A2A server for an ASP.NET Core app.</summary>
public static class A2AServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the A2A server. It calls <c>AddA2AServerCore</c> (see <c>Ambinder.A2A.Server</c>) and adds the ASP.NET Core parts. Registers: options, agent catalog, in-memory task store, event broker, the traced
    /// <see cref="Ambinder.A2A.Server.IA2ARequestHandler"/>, push delivery (config store, signing key, sender), <see cref="IAgentAuthorizer"/>,
    /// <see cref="IOwnerResolver"/> (default <see cref="DefaultOwnerResolver"/>) and a startup check
    /// (unknown policy, default agent or gRPC port key → <see cref="InvalidOperationException"/> before the server listens).
    /// <see cref="A2ASecurityOptions"/> are bound from the <c>A2A:Security</c> configuration section.
    /// Authentication schemes are the app's job; call <c>UseAuthentication()</c> before <c>MapA2A()</c>.
    /// gRPC is opt-in: add <c>Ambinder.A2A.AspNetCore.Grpc</c> and call <c>AddA2AGrpc()</c> on the returned builder.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the <see cref="A2AServerOptions"/>. At least <see cref="A2AServerOptions.PublicBaseUrl"/> is required.</param>
    /// <returns>A builder to add agents to.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Lifetimes: all singletons, except the startup check, which is a transient <c>IStartupFilter</c>. Registered with <c>TryAdd</c>
    /// (a service you register first wins): <see cref="IAgentAuthorizer"/>, the card renderer and the caller resolver, plus everything
    /// <c>AddA2AServerCore</c> registers with <c>TryAdd</c>. <c>AddAuthorization()</c> is called for you; add your own authentication.
    /// </remarks>
    /// <example>
    /// <code>
    /// // requires: WebApplicationBuilder builder;
    /// builder.Services.AddA2AServer(o => o.PublicBaseUrl = "http://localhost:5200")
    ///     .AddAgent&lt;EchoAgent&gt;("echo", sp => new AgentCard { Name = "Echo", Description = "Repeats text.", Version = "1.0.0" });
    /// var app = builder.Build();
    /// app.MapA2A();
    /// app.Run();
    /// </code>
    /// </example>
    public static A2AServerBuilder AddA2AServer(this IServiceCollection services, Action<A2AServerOptions> configure)
    {
        var builder = services.AddA2AServerCore(configure);
        services.AddAuthorization();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<A2ASecurityOptions>, SecurityOptionsConfiguration>());
        services.TryAddSingleton<IAgentAuthorizer, AgentAuthorizer>();
        services.TryAddSingleton<IA2ACallerResolver, DefaultA2ACallerResolver>();
        services.TryAddSingleton<DevelopmentAnonymousAgents>();
        services.TryAddSingleton<AgentCardRenderer>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, A2AStartupValidation>());
        return builder;
    }
}
