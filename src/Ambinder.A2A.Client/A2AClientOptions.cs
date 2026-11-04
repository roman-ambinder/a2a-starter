using System.Security.Cryptography.X509Certificates;
using Ambinder.A2A.Client.Auth;

namespace Ambinder.A2A.Client;

/// <summary>Options shared by all client transports.</summary>
public sealed class A2AClientOptions
{
    /// <summary>HTTP client to use. When null the client creates (and disposes) its own.</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>Supplies auth headers for every call. Null → anonymous.</summary>
    public IA2ACredentialProvider? Credentials { get; init; }

    /// <summary>Extension URIs sent in <c>A2A-Extensions</c> on every call.</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>Preferred binding (<c>JSONRPC</c>, <c>HTTP+JSON</c>, <c>GRPC</c>) for the client factory.</summary>
    public string? PreferredBinding { get; init; }

    /// <summary>
    /// Transports for bindings beyond the built-in JSON-RPC and HTTP+JSON, for example <c>GrpcClientTransport.Instance</c> from the
    /// <c>Ambinder.A2A.Client.Grpc</c> package. A card interface is used only when its binding is built in or listed here; a transport
    /// listed here replaces the built-in one for the same binding. Default: empty.
    /// </summary>
    public IReadOnlyList<IA2AClientTransport> Transports { get; init; } = [];

    /// <summary>
    /// TLS for a gRPC interface whose card URL is a bare <c>host:port</c> (the spec's gRPC address form). Null (default): plain
    /// text (h2c) for loopback hosts, TLS for any other host. A card URL with <c>http://</c> or <c>https://</c> decides by itself.
    /// </summary>
    public bool? GrpcUseTls { get; init; }

    /// <summary>Client certificate for mutual TLS. Used only when the client creates its own HttpClient.</summary>
    public X509Certificate2? ClientCertificate { get; init; }
}
