using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client.Transports.JsonRpc;

/// <summary>Creates <see cref="JsonRpcTransport"/> instances for the <c>JSONRPC</c> binding at protocol version 1.0.</summary>
internal sealed class JsonRpcTransportFactory : IA2ATransportFactory
{
    /// <inheritdoc />
    public IReadOnlyList<string> Bindings { get; } = [A2AConstants.BindingJsonRpc];

    /// <inheritdoc />
    public IReadOnlyList<string> ProtocolVersions { get; } = ["1.0"];

    /// <inheritdoc />
    public IA2ATransport Create(AgentInterface selected, AgentCard card, A2ATransportContext context)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(context);
        if (!Uri.TryCreate(selected.Url, UriKind.Absolute, out var endpoint))
            throw new ArgumentException($"The JSON-RPC interface URL of agent '{context.ClientName}' is not an absolute URL.", nameof(selected));
        return new JsonRpcTransport(endpoint, context);
    }
}
