using System.Security.Cryptography.X509Certificates;
using A2A.Client.Auth;
using A2A.Protocol;

namespace A2A.Client;

/// <summary>Options shared by all client transports.</summary>
public sealed class A2AClientOptions
{
    /// <summary>HTTP client to use. When null the client creates (and disposes) its own.</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>Supplies auth headers for every call. Null → anonymous.</summary>
    public IA2ACredentialProvider? Credentials { get; init; }

    /// <summary>Extension URIs sent in <c>A2A-Extensions</c> on every call.</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>Value of the <c>A2A-Version</c> header.</summary>
    public string ProtocolVersion { get; init; } = A2AConstants.ProtocolVersion;

    /// <summary>Preferred binding (<c>JSONRPC</c>, <c>HTTP+JSON</c>, <c>GRPC</c>) for the client factory.</summary>
    public string? PreferredBinding { get; init; }

    /// <summary>Client certificate for mutual TLS. Used only when the client creates its own HttpClient.</summary>
    public X509Certificate2? ClientCertificate { get; init; }
}
