using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client;

/// <summary>
/// Creates clients for one protocol binding. <see cref="A2AClientFactory"/> builds JSON-RPC and HTTP+JSON clients itself; other
/// bindings (gRPC, from the <c>Ambinder.A2A.Client.Grpc</c> package) come from implementations listed in
/// <see cref="A2AClientOptions.Transports"/>.
/// </summary>
public interface IA2AClientTransport
{
    /// <summary>The card <c>protocolBinding</c> this transport serves, for example <c>GRPC</c> (compared case-insensitively).</summary>
    string ProtocolBinding { get; }

    /// <summary>The <c>a2a.transport</c> telemetry value for clients of this transport, for example <see cref="A2ATelemetryNames.TransportGrpc"/>.</summary>
    string TransportName { get; }

    /// <summary>Creates a client for the card interface the factory selected.</summary>
    /// <param name="agentInterface">The selected interface (binding, URL, tenant, version).</param>
    /// <param name="card">The agent card; handed to the credential provider.</param>
    /// <param name="options">The client options.</param>
    /// <param name="tenant">The interface's tenant, or null: sent when a request's tenant is empty.</param>
    /// <exception cref="NotSupportedException">The interface URL cannot be used by this transport.</exception>
    IA2AClient Create(AgentInterface agentInterface, AgentCard card, A2AClientOptions options, string? tenant);
}
