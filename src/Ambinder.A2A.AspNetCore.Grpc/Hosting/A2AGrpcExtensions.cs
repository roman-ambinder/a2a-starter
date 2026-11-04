using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ambinder.A2A.Server.Hosting;

namespace Ambinder.A2A.AspNetCore.Grpc;

public static class A2AGrpcExtensions
{
    /// <summary>
    /// Adds the gRPC services the A2A gRPC endpoint needs (<c>AddGrpc()</c>). Call it on the builder returned by
    /// <c>AddA2AServer</c>, then map the endpoint with <see cref="MapA2AGrpc"/>.
    /// </summary>
    public static A2AServerBuilder AddA2AGrpc(this A2AServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddGrpc();
        return builder;
    }

    /// <summary>
    /// Maps the A2A gRPC service (<see cref="A2AGrpcService"/>). Requires <c>AddA2AServer(...).AddA2AGrpc()</c> and
    /// <c>UseAuthentication()</c> before it when agents have policies. The agent is picked by the connection's local port.
    /// </summary>
    public static GrpcServiceEndpointConventionBuilder MapA2AGrpc(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapGrpcService<A2AGrpcService>();
    }
}
