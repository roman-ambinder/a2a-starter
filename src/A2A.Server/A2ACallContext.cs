using System.Security.Claims;

namespace A2A.Server;

/// <summary>
/// Per-call information the transport passes to <see cref="IA2ARequestHandler"/>.
/// </summary>
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
    IReadOnlyList<string> RequestedExtensions);
