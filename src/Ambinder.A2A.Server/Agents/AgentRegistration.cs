using System.Diagnostics.CodeAnalysis;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Security;

namespace Ambinder.A2A.Server.Agents;

/// <summary>One hosted agent.</summary>
/// <remarks>Hosting builds registrations from <see cref="Hosting.A2AServerBuilder.AddAgent{THandler}"/>. The values do not change after startup.</remarks>
/// <param name="Name">Catalog name; used in URLs.</param>
/// <param name="HandlerType">A type implementing <see cref="IAgentHandler"/>.</param>
/// <param name="PublicCard">The card served to everyone.</param>
/// <param name="ExtendedCard">The authenticated extended card, or null.</param>
/// <param name="AuthorizationPolicy">ASP.NET Core authorization policy name, or null for none.</param>
public sealed record AgentRegistration(
    string Name,
    [param: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    Type HandlerType,
    AgentCard PublicCard,
    AgentCard? ExtendedCard,
    string? AuthorizationPolicy)
{
    /// <summary>
    /// Who may call the ordinary operations of this agent, or <see langword="null"/> when no mode was set. A null mode means
    /// <see cref="AgentAccess.Authenticated"/>, except in the Development environment when no authentication scheme is
    /// registered at all: then the agent runs as <see cref="AgentAccess.Anonymous"/> and startup logs one warning.
    /// </summary>
    public AgentAccess? Access { get; init; }
}
