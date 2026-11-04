using Microsoft.Extensions.Logging;

namespace Ambinder.A2A.Client;

/// <summary>The shared client services an <see cref="IA2ATransportFactory"/> gives to the transport it creates.</summary>
/// <remarks>The client creates one context per client. A transport can keep it for its own lifetime.</remarks>
public sealed class A2ATransportContext
{
    /// <summary>Gets the client name, used in messages (<c>default</c> for clients made without a name).</summary>
    public required string ClientName { get; init; }

    /// <summary>Gets the named <c>HttpClient</c>. HTTP transports send through it. The client owns it, so a transport must not dispose it.</summary>
    public required HttpClient HttpClient { get; init; }

    /// <summary>
    /// Gets the handler factory, or <see langword="null"/> when the client does not use <c>IHttpClientFactory</c>. A gRPC transport
    /// builds its channel from the <c>A2A:{name}:grpc</c> handler chain.
    /// </summary>
    public required IHttpMessageHandlerFactory? HandlerFactory { get; init; }

    /// <summary>Gets the size limits for responses, events and messages.</summary>
    public required A2AClientLimits Limits { get; init; }

    /// <summary>Gets the per-operation timeouts the client applies, so a transport can bound its own work to match.</summary>
    public required A2AClientTimeouts Timeouts { get; init; }

    /// <summary>Gets the logger for the transport. Never log message content, header values, tokens or query strings.</summary>
    public required ILogger Logger { get; init; }

    /// <summary>Gets the time source. Use it instead of the system clock so tests can control time.</summary>
    public required TimeProvider Time { get; init; }
}
