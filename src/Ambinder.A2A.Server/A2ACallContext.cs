using Ambinder.A2A.Server.Security;

namespace Ambinder.A2A.Server;

/// <summary>
/// Per-call information the transport passes to <see cref="IA2ARequestHandler"/>.
/// </summary>
/// <remarks>Transports create this value. Create one yourself only to call an <see cref="IA2ARequestHandler"/> in process, for example in a test. Use an object initializer.</remarks>
public sealed record A2ACallContext
{
    /// <summary>The agent addressed by the call (from the URL or gRPC port). Must exist in the catalog.</summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// The caller: owner key, kind and Entra identity, from the registered <see cref="IOwnerResolver"/>. Its
    /// <see cref="A2ACaller.Principal"/> is a snapshot that stays valid after the request ends.
    /// </summary>
    public required A2ACaller Caller { get; init; }

    /// <summary>The resolved tenant of the call. An empty string means no tenant.</summary>
    public required string Tenant { get; init; }

    /// <summary>The A2A version the caller sent (already checked by the transport).</summary>
    public string? ProtocolVersion { get; init; }

    /// <summary>Extension URIs the caller asked for (<c>A2A-Extensions</c> header).</summary>
    public IReadOnlyList<string> RequestedExtensions { get; init; } = [];

    /// <summary>The transport that carried the call: <c>jsonrpc</c>, <c>rest</c> or <c>grpc</c>. Null when called in process.</summary>
    public string? Transport { get; init; }
}
