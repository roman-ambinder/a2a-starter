using System.Security.Claims;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;

namespace Ambinder.A2A.Server.Security;

/// <summary>
/// Turns the authenticated principal of a request into an <see cref="A2ACaller"/>, including the owner key that decides who may
/// see a task. Register your own before <c>AddA2AServer</c> to support another authentication scheme.
/// </summary>
/// <remarks>
/// The resolver is registered as a singleton and called once per request, so calls can overlap: an implementation must be
/// thread-safe and fast. The owner key must be stable: the same caller must always get the same key, or it loses access to its
/// tasks. To support one more scheme, wrap <see cref="DefaultOwnerResolver"/> and handle your scheme first.
/// </remarks>
public interface IOwnerResolver
{
    /// <summary>Returns the caller for <paramref name="user"/>.</summary>
    /// <param name="user">The principal of the request. It may be unauthenticated.</param>
    /// <param name="agent">The agent that is called.</param>
    /// <param name="ct">A token that cancels the resolution.</param>
    /// <returns>The caller.</returns>
    /// <exception cref="A2AAuthException">The caller cannot be identified (HTTP 401). The message says what is missing.</exception>
    ValueTask<A2ACaller> ResolveAsync(ClaimsPrincipal user, AgentRegistration agent, CancellationToken ct);
}
