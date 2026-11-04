using Ambinder.A2A.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Turns on the gRPC binding of the A2A server.</summary>
public static class A2AGrpcServerBuilderExtensions
{
    /// <summary>
    /// Adds the gRPC services the A2A gRPC endpoint needs (<c>AddGrpc()</c>). Call it on the builder returned by
    /// <c>AddA2AServer</c>, then map the endpoint with <c>MapA2AGrpc</c>.
    /// </summary>
    /// <param name="builder">The builder returned by <c>AddA2AServer</c>.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>Registers the ASP.NET Core gRPC services through <c>AddGrpc()</c>. Give each gRPC agent a port in <see cref="A2AServerOptions.GrpcPorts"/>.</remarks>
    /// <example>
    /// <code>
    /// // requires: IServiceCollection services;
    /// services.AddA2AServer(o =>
    /// {
    ///     o.PublicBaseUrl = "http://localhost:5200";
    ///     o.GrpcPorts["echo"] = 5201;
    /// }).AddA2AGrpc();
    /// </code>
    /// </example>
    public static A2AServerBuilder AddA2AGrpc(this A2AServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddGrpc();
        return builder;
    }
}
