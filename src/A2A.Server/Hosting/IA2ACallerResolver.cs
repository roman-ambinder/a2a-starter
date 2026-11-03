using System.Security.Claims;
using A2A.Protocol;
using Microsoft.AspNetCore.Http;

namespace A2A.Server.Hosting;

/// <summary>Resolves the caller and tenant of an A2A request, the same way the core transports do.</summary>
/// <remarks>The core registers one implementation and all its transports use it. Singleton; calls can run concurrently.</remarks>
internal interface IA2ACallerResolver
{
    /// <summary>Authenticates the request and resolves its tenant for the agent.</summary>
    /// <param name="http">The request.</param>
    /// <param name="agentName">The agent the request is for.</param>
    /// <param name="requestTenant">The tenant the request names, or <see langword="null"/>.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The caller and the resolved tenant (empty when there is none).</returns>
    /// <exception cref="A2AAuthException">The caller is not authenticated, or may not call this agent.</exception>
    ValueTask<(DispatchCaller Caller, string Tenant)> ResolveAsync(HttpContext http, string agentName, string? requestTenant, CancellationToken ct);
}

/// <summary>
/// The caller of a request: the principal and the task owner key. Stands in for the future <c>A2ACaller</c> type and is
/// replaced by it when that type lands.
/// </summary>
/// <param name="User">The authenticated (or anonymous) principal.</param>
/// <param name="Owner">The task owner key, from the registered owner resolver.</param>
internal sealed record DispatchCaller(ClaimsPrincipal User, string Owner);
