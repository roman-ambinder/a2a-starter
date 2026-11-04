using System.Security.Claims;

namespace Ambinder.A2A.Server;

/// <summary>
/// Per-call information the transport passes to <see cref="IA2ARequestHandler"/>.
/// </summary>
/// <remarks>Transports create this value. Create one yourself only to call an <see cref="IA2ARequestHandler"/> in process, for example in a test.</remarks>
/// <param name="AgentName">The agent addressed by the call (from the URL or gRPC port). Must exist in the catalog.</param>
/// <param name="User">The authenticated (or anonymous) caller.</param>
/// <param name="Owner">Task owner key for the caller, from the registered <see cref="Auth.IOwnerResolver"/>.</param>
/// <param name="Tenant">The request's <c>tenant</c> field. Stored on the task; Phase 1 does not isolate by it.</param>
/// <param name="ProtocolVersion">The A2A version the caller sent (already checked by the transport).</param>
/// <param name="RequestedExtensions">Extension URIs the caller asked for (A2A-Extensions header).</param>
public sealed record A2ACallContext(
    string AgentName,
    ClaimsPrincipal User,
    string Owner,
    string? Tenant,
    string? ProtocolVersion,
    IReadOnlyList<string> RequestedExtensions)
{
    /// <summary>The transport that carried the call: <c>jsonrpc</c>, <c>rest</c> or <c>grpc</c>. Null when called in process.</summary>
    public string? Transport { get; init; }

    /// <summary>
    /// <see langword="true"/> when another server instance already ran the pipeline for this call and forwarded it here. The pipeline then runs
    /// only tracing and the operation. Set by the cluster layer; leave it <see langword="false"/> otherwise.
    /// </summary>
    public bool IsForwarded { get; init; }

    /// <summary>
    /// The extensions that are active for the call (requested and declared by the agent card). The extension negotiation middleware computes them
    /// and the pipeline's last step passes them on in a copy of this context (<c>with</c>), so the task manager reads the list from the call.
    /// Leave it <see langword="null"/> when you create a context: the task manager then derives the list from <see cref="RequestedExtensions"/>
    /// without the required-extension check, which is also what a forwarded call gets.
    /// </summary>
    public IReadOnlyList<string>? ActiveExtensions { get; init; }

    /// <summary>The request scope. Transports pass the scope of the HTTP request. When null (in-process calls) the pipeline creates a scope for the call.</summary>
    public IServiceProvider? Services { get; init; }

    /// <summary>Where middleware writes response headers. Transports pass one and send it before the body. Null in-process, where the pipeline uses a private instance.</summary>
    public A2AResponseMetadata? ResponseMetadata { get; init; }
}
