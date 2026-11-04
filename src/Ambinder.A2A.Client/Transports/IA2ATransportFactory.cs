using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client;

/// <summary>
/// Creates transports for one or more (protocol binding, protocol version) pairs. This is the seam for bindings and protocol
/// versions beyond the built-in JSON-RPC and HTTP+JSON 1.0 transports, for example gRPC or A2A 0.3.
/// </summary>
/// <remarks>
/// <para>
/// Lifetime: register one factory instance per client (or per service provider). The client may keep the factory for as long as
/// it lives, and it may call <see cref="Create"/> once per selected interface, so keep the factory itself cheap and stateless. A
/// factory that is registered as a singleton must not hold per-call state.
/// </para>
/// <para>
/// Concurrency: <see cref="Bindings"/>, <see cref="ProtocolVersions"/> and <see cref="Create"/> can be called from any thread at
/// the same time. Implementations must be thread-safe.
/// </para>
/// <para>
/// The client does everything around the transport (interceptors, credentials, auth retry, resilience, timeouts, tracing, limits
/// and the tenant). A transport only maps a request to the wire and back.
/// </para>
/// </remarks>
public interface IA2ATransportFactory
{
    /// <summary>
    /// Gets the protocol bindings this factory serves, for example <c>JSONRPC</c> or <c>HTTP+JSON</c>. The client compares them
    /// with the card's <c>protocolBinding</c> case-insensitively.
    /// </summary>
    IReadOnlyList<string> Bindings { get; }

    /// <summary>Gets the protocol versions this factory serves, as <c>Major.Minor</c>, for example <c>1.0</c>.</summary>
    IReadOnlyList<string> ProtocolVersions { get; }

    /// <summary>Creates a transport for the interface the client selected.</summary>
    /// <param name="selected">The selected card interface (binding, URL, tenant and version).</param>
    /// <param name="card">The agent card the interface came from.</param>
    /// <param name="context">The shared client services the transport can use.</param>
    /// <returns>A transport for the interface. The client owns it and disposes it.</returns>
    /// <exception cref="NotSupportedException">The interface cannot be used by this factory, for example its URL has an unsupported scheme.</exception>
    IA2ATransport Create(AgentInterface selected, AgentCard card, A2ATransportContext context);
}
