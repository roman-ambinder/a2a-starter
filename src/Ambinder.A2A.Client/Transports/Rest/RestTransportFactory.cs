using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client.Transports.Rest;

/// <summary>Creates <see cref="RestTransport"/> instances for the <c>HTTP+JSON</c> binding at protocol version 1.0.</summary>
internal sealed class RestTransportFactory : IA2ATransportFactory
{
    /// <inheritdoc />
    public IReadOnlyList<string> Bindings { get; } = [A2AConstants.BindingRest];

    /// <inheritdoc />
    public IReadOnlyList<string> ProtocolVersions { get; } = ["1.0"];

    /// <inheritdoc />
    public IA2ATransport Create(AgentInterface selected, AgentCard card, A2ATransportContext context)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(context);
        if (!Uri.TryCreate(selected.Url, UriKind.Absolute, out var baseUrl))
            throw new ArgumentException($"The REST interface URL of agent '{context.ClientName}' is not an absolute URL.", nameof(selected));
        return new RestTransport(baseUrl, context);
    }
}
