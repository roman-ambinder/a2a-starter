using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

public static class A2AGrpcEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the A2A gRPC service (<see cref="Ambinder.A2A.AspNetCore.Grpc.A2AGrpcService"/>). Requires <c>AddA2AServer(...).AddA2AGrpc()</c> and
    /// <c>UseAuthentication()</c> before it when agents have policies. The agent is picked by the connection's local port.
    /// </summary>
    public static GrpcServiceEndpointConventionBuilder MapA2AGrpc(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapGrpcService<Ambinder.A2A.AspNetCore.Grpc.A2AGrpcService>();
    }
}
