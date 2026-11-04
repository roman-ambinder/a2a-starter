using Ambinder.A2A.Server.Security;
using Ambinder.A2A.Protocol;
using Microsoft.AspNetCore.Http;

namespace Ambinder.A2A.AspNetCore.Hosting;

/// <summary>Resolves the caller and tenant of an A2A request, the same way the core transports do.</summary>
/// <remarks>
/// The core registers one implementation (<c>AddA2AServer</c> uses <c>TryAdd</c>, so register your own first to replace it) and all
/// its transports, including gRPC, use it. A separate compat package uses it too, so that every protocol version applies the same
/// caller and tenant rules. Register implementations as singletons; calls can run concurrently.
/// </remarks>
public interface IA2ACallerResolver
{
    /// <summary>Authenticates the request and resolves its tenant for the agent.</summary>
    /// <param name="http">The request.</param>
    /// <param name="agentName">The agent the request is for.</param>
    /// <param name="requestTenant">The tenant the request names, or <see langword="null"/>.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The caller and the resolved tenant (empty when there is none).</returns>
    /// <exception cref="A2AException"><c>InvalidParams</c>: the request names a different tenant than the agent's interface tenant.</exception>
    /// <exception cref="A2AAuthException">The caller is not authenticated, or may not call this agent.</exception>
    ValueTask<(A2ACaller Caller, string Tenant)> ResolveAsync(HttpContext http, string agentName, string? requestTenant, CancellationToken ct);
}
