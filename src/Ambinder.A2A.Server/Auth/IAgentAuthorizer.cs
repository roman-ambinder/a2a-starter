using System.Security.Claims;
using Ambinder.A2A.Server.Agents;

namespace Ambinder.A2A.Server.Auth;

/// <summary>Checks a caller against an agent's authorization policy. Transports call it for every operation (cards excepted).</summary>
/// <remarks>The default implementation is registered as a singleton (the default is added with <c>TryAdd</c>, so register your own before <c>AddA2AServer</c> to replace it). Calls can overlap, so an implementation must be thread-safe.</remarks>
public interface IAgentAuthorizer
{
    /// <summary>
    /// No policy → allowed (anonymous too). Policy and unauthenticated caller → <see cref="Protocol.A2AAuthException"/> (401).
    /// Policy not met → <see cref="Protocol.A2AAuthException"/> (403).
    /// </summary>
    /// <param name="agent">The agent that is called.</param>
    /// <param name="user">The caller.</param>
    /// <param name="ct">A token that cancels the check.</param>
    /// <returns>A task that completes when the caller is allowed.</returns>
    /// <exception cref="Protocol.A2AAuthException">The caller is not authenticated (401) or not allowed (403).</exception>
    Task AuthorizeAsync(AgentRegistration agent, ClaimsPrincipal user, CancellationToken ct);
}
