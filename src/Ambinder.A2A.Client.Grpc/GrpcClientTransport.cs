using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client.Grpc;

/// <summary>
/// The gRPC binding for <see cref="A2AClientFactory"/>. Add <see cref="Instance"/> to <see cref="A2AClientOptions.Transports"/>;
/// a card interface with binding <c>GRPC</c> then yields a gRPC client.
/// </summary>
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
    /// <exception cref="NotSupportedException">The interface URL is not a valid http or https address.</exception>
    public IA2AClient Create(AgentInterface agentInterface, AgentCard card, A2AClientOptions options, string? tenant)
    {
        ArgumentNullException.ThrowIfNull(agentInterface);
        var address = GrpcAddress(agentInterface.Url, options.GrpcUseTls);
        if (!Uri.TryCreate(address, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new NotSupportedException($"The agent card's {agentInterface.ProtocolBinding} interface has an invalid URL: '{agentInterface.Url}'.");
        return new GrpcA2AClient(url, card, options, tenant);
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
