using System.Diagnostics.CodeAnalysis;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Agents;

/// <summary>One hosted agent.</summary>
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
    string? AuthorizationPolicy);
