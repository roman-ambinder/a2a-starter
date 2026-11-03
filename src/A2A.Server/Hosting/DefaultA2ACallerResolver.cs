using A2A.Server.Agents;
using A2A.Server.Auth;
using Microsoft.AspNetCore.Http;

namespace A2A.Server.Hosting;

/// <summary>
/// Runs the agent's policy (<see cref="IAgentAuthorizer"/>), then takes the owner from <see cref="IOwnerResolver"/>.
/// The tenant is the request's tenant, or empty.
/// </summary>
internal sealed class DefaultA2ACallerResolver(AgentCatalog catalog, IAgentAuthorizer authorizer, IOwnerResolver owners) : IA2ACallerResolver
{
    public async ValueTask<(DispatchCaller Caller, string Tenant)> ResolveAsync(HttpContext http, string agentName, string? requestTenant,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!catalog.TryGet(agentName, out var agent))
            throw new ArgumentException($"Unknown agent '{agentName}'.", nameof(agentName));
        await authorizer.AuthorizeAsync(agent, http.User, ct).ConfigureAwait(false);
        return (new DispatchCaller(http.User, owners.GetOwner(http.User)), requestTenant ?? "");
    }
}
