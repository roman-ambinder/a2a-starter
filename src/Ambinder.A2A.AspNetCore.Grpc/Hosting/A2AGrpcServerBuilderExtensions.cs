using Ambinder.A2A.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class A2AGrpcServerBuilderExtensions
{
    /// <summary>
    /// Adds the gRPC services the A2A gRPC endpoint needs (<c>AddGrpc()</c>). Call it on the builder returned by
    /// <c>AddA2AServer</c>, then map the endpoint with <c>MapA2AGrpc</c>.
    /// </summary>
    public static A2AServerBuilder AddA2AGrpc(this A2AServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddGrpc();
        return builder;
    }
}
