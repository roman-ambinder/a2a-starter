using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps the A2A gRPC endpoint into an ASP.NET Core app.</summary>
public static class A2AGrpcEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the A2A gRPC service (<see cref="Ambinder.A2A.AspNetCore.Grpc.A2AGrpcService"/>). Requires <c>AddA2AServer(...).AddA2AGrpc()</c> and
    /// <c>UseAuthentication()</c> before it when agents have policies. The agent is picked by the connection's local port.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder, usually the <c>WebApplication</c>.</param>
    /// <returns>The builder for the gRPC endpoint, so callers can add conventions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// // requires: WebApplication app;
    /// app.MapA2AGrpc();
    /// </code>
    /// </example>
    public static GrpcServiceEndpointConventionBuilder MapA2AGrpc(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapGrpcService<Ambinder.A2A.AspNetCore.Grpc.A2AGrpcService>();
    }
}
