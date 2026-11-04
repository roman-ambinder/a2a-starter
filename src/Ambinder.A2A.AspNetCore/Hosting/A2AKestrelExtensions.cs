using System.Net;
using Ambinder.A2A.Server.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Configures Kestrel for A2A.</summary>
public static class A2AKestrelExtensions
{
    /// <summary>
    /// Configures Kestrel for A2A: the port of <see cref="A2AServerOptions.PublicBaseUrl"/> with HTTP/1.1 and HTTP/2, and one
    /// HTTP/2-only plaintext (h2c) listener per <see cref="A2AServerOptions.GrpcPorts"/> value. Addresses follow the base URL host:
    /// an IP literal binds that address, <c>localhost</c> binds loopback (IPv4 and IPv6), any other host binds all interfaces.
    /// Options are read when the server starts, so <c>AddA2AServer</c> may be called before or after this. Throws
    /// <see cref="InvalidOperationException"/> at startup when two listeners would share a port.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>Needed only when you serve gRPC, because gRPC agents listen on their own HTTP/2 ports. Without gRPC you can configure Kestrel yourself.</remarks>
    /// <example>
    /// <code>
    /// // requires: WebApplicationBuilder builder;
    /// builder.ConfigureA2AKestrel();
    /// </code>
    /// </example>
    public static WebApplicationBuilder ConfigureA2AKestrel(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            var options = kestrel.ApplicationServices.GetRequiredService<IOptions<A2AServerOptions>>().Value;
            var baseUri = new Uri(options.PublicBaseUrl.TrimEnd('/'));
            var ports = options.GrpcPorts.Values.Append(baseUri.Port).ToList();
            if (ports.Distinct().Count() != ports.Count)
                throw new InvalidOperationException($"The HTTP port of A2AServerOptions.PublicBaseUrl ({baseUri.Port}) and the gRPC ports in A2AServerOptions.GrpcPorts ({string.Join(", ", options.GrpcPorts.Values)}) must all be different. " +
                    "Set a different port in PublicBaseUrl or give each agent its own port in GrpcPorts.");

            Listen(kestrel, baseUri.Host, baseUri.Port, HttpProtocols.Http1AndHttp2);
            foreach (var port in options.GrpcPorts.Values)
                Listen(kestrel, baseUri.Host, port, HttpProtocols.Http2);
        });
        return builder;
    }

    private static void Listen(KestrelServerOptions kestrel, string host, int port, HttpProtocols protocols)
    {
        void Configure(ListenOptions listen) => listen.Protocols = protocols;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var ip)) kestrel.Listen(ip, port, Configure);
        else if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) kestrel.ListenLocalhost(port, Configure);
        else kestrel.ListenAnyIP(port, Configure);
    }
}
