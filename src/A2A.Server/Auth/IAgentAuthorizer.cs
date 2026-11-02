using System.Security.Claims;
using A2A.Server.Agents;

namespace A2A.Server.Auth;

/// <summary>Checks a caller against an agent's authorization policy. Transports call it for every operation (cards excepted).</summary>
public interface IAgentAuthorizer
{
    /// <summary>
    /// No policy → allowed (anonymous too). Policy and unauthenticated caller → <see cref="Protocol.A2AAuthException"/> (401).
    /// Policy not met → <see cref="Protocol.A2AAuthException"/> (403).
    /// </summary>
    Task AuthorizeAsync(AgentRegistration agent, ClaimsPrincipal user, CancellationToken ct);
}
