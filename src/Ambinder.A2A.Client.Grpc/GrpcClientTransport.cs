using Ambinder.A2A.Client.Transports;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client.Grpc;

/// <summary>
/// The gRPC binding for <see cref="A2AClientFactory"/>. Add <see cref="Instance"/> to <see cref="A2AClientOptions.Transports"/>;
/// a card interface with binding <c>GRPC</c> then yields a gRPC client.
/// </summary>
/// <remarks>
/// This is the interim way to turn on gRPC. A later preview replaces it. The transport holds no state and is thread-safe.
/// </remarks>
/// <example>
/// <code>
/// // requires: AgentCard card;
/// var options = new A2AClientOptions
/// {
///     PreferredBinding = A2AConstants.BindingGrpc,
///     Transports = [GrpcClientTransport.Instance],
/// };
/// await using IA2AClient client = A2AClientFactory.Create(card, options);
/// </code>
/// </example>
public sealed class GrpcClientTransport : IA2AClientTransport
{
    /// <summary>The shared instance (the transport holds no state).</summary>
    public static GrpcClientTransport Instance { get; } = new();

    private GrpcClientTransport() { }

    /// <inheritdoc />
    public string ProtocolBinding => A2AConstants.BindingGrpc;

    /// <inheritdoc />
    public string TransportName => A2ATelemetryNames.TransportGrpc;

    /// <summary>
    /// Creates a gRPC client. A card URL with <c>http://</c> or <c>https://</c> is used as is; a bare <c>host:port</c>
    /// gets <c>https://</c>, or <c>http://</c> when <see cref="A2AClientOptions.GrpcUseTls"/> is false or (when null) the host is loopback.
    /// </summary>
    /// <param name="agentInterface">The selected card interface.</param>
    /// <param name="card">The agent card; handed to the credential provider.</param>
    /// <param name="options">The client options.</param>
    /// <param name="tenant">The tenant of the interface, or <see langword="null"/>. It is sent when a request has no tenant.</param>
    /// <returns>A gRPC client. The caller owns it and disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="agentInterface"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The interface URL is not a valid http or https address.</exception>
    public IA2AClient Create(AgentInterface agentInterface, AgentCard card, A2AClientOptions options, string? tenant)
    {
        ArgumentNullException.ThrowIfNull(agentInterface);
        var address = GrpcAddress(agentInterface.Url, options.GrpcUseTls);
        if (!Uri.TryCreate(address, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new NotSupportedException($"The agent card's {agentInterface.ProtocolBinding} interface has an invalid URL: '{agentInterface.Url}'.");
        // The a2a-version metadata is the selected interface's Major.Minor; an empty interface version means the current one.
        return new GrpcA2AClient(url, card, options, tenant, ClientSupport.MajorMinor(agentInterface.ProtocolVersion) ?? A2AConstants.ProtocolVersion);
    }

    /// <summary>
    /// A gRPC interface URL as a channel address. <c>http://</c> / <c>https://</c> URLs are used as is. A bare <c>host:port</c>
    /// gets <c>https://</c>, or <c>http://</c> when <paramref name="useTls"/> is false, or (null) when the host is loopback.
    /// </summary>
    internal static string GrpcAddress(string url, bool? useTls)
    {
        if (url.Contains("://", StringComparison.Ordinal)) return url;
        var tls = useTls ?? !IsLoopback(url);
        return (tls ? "https://" : "http://") + url;
    }

    private static bool IsLoopback(string hostPort)
    {
        if (!Uri.TryCreate("http://" + hostPort, UriKind.Absolute, out var uri)) return false;
        return uri.IsLoopback;
    }
}
