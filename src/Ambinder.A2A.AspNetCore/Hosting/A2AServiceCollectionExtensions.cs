using Ambinder.A2A.AspNetCore.Auth;
using Ambinder.A2A.Server.Auth;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Server.Tasks;
using Ambinder.A2A.Server.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ambinder.A2A.AspNetCore.Hosting;

public static class A2AAspNetCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the A2A server: options, agent catalog, in-memory task store, event broker, <see cref="TaskManager"/>
    /// wrapped in <see cref="TracingRequestHandler"/> as <see cref="IA2ARequestHandler"/>, push delivery (config store, signing key, sender), <see cref="IAgentAuthorizer"/>,
    /// <see cref="IOwnerResolver"/> (default <see cref="DefaultOwnerResolver"/>) and a startup check
    /// (unknown policy, default agent or gRPC port key → <see cref="InvalidOperationException"/> before the server listens).
    /// Authentication schemes are the app's job; call <c>UseAuthentication()</c> before <c>MapA2A()</c>.
    /// gRPC is opt-in: add <c>Ambinder.A2A.AspNetCore.Grpc</c> and call <c>AddA2AGrpc()</c> on the returned builder.
    /// </summary>
    public static A2AServerBuilder AddA2AServer(this IServiceCollection services, Action<A2AServerOptions> configure)
    {
        var builder = services.AddA2AServerCore(configure);
        services.AddAuthorization();
        services.TryAddSingleton<IAgentAuthorizer, AgentAuthorizer>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, A2AStartupValidation>());
        return builder;
    }
}
