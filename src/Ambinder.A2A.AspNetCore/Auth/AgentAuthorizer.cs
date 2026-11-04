using System.Security.Claims;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Ambinder.A2A.AspNetCore.Auth;

/// <summary>
/// <see cref="IAgentAuthorizer"/> over ASP.NET Core <see cref="IAuthorizationService"/>. The policy is evaluated against the
/// principal the authentication middleware produced (the default scheme); policy-level <c>AuthenticationSchemes</c> are not re-run.
/// </summary>
internal sealed class AgentAuthorizer(IAuthorizationService authorization) : IAgentAuthorizer
{
    public async Task AuthorizeAsync(AgentRegistration agent, ClaimsPrincipal user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(user);
        if (agent.AuthorizationPolicy is not { } policy) return;
        if (!IsAuthenticated(user)) throw new A2AAuthException(401, "Authentication is required.");
        ct.ThrowIfCancellationRequested();
        var result = await authorization.AuthorizeAsync(user, resource: agent, policy).ConfigureAwait(false);
        if (!result.Succeeded) throw new A2AAuthException(403, $"The caller is not allowed to use agent '{agent.Name}'.");
    }

    /// <summary>True when the primary identity is authenticated (the rule is shared with the gRPC package).</summary>
    public static bool IsAuthenticated(ClaimsPrincipal user) => Transports.TransportRequest.IsAuthenticated(user);
}
