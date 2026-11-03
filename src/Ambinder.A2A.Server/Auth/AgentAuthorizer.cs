using System.Security.Claims;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Microsoft.AspNetCore.Authorization;

namespace Ambinder.A2A.Server.Auth;

/// <summary>
/// <see cref="IAgentAuthorizer"/> over ASP.NET Core <see cref="IAuthorizationService"/>. The policy is evaluated against the
/// principal the authentication middleware produced (the default scheme); policy-level <c>AuthenticationSchemes</c> are not re-run.
/// </summary>
public sealed class AgentAuthorizer(IAuthorizationService authorization) : IAgentAuthorizer
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

    /// <summary>
    /// True when the primary identity (<see cref="ClaimsPrincipal.Identity"/>) is authenticated. The same rule as
    /// <see cref="DefaultOwnerResolver.GetOwner"/>, so a caller is never authorized here and then treated as anonymous for ownership.
    /// </summary>
    public static bool IsAuthenticated(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true;
}
