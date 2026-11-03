using System.Text.Json.Nodes;
using A2A.Protocol;

namespace A2A.Server.Hosting;

/// <summary>
/// Adds members to an agent card before it is served. Register implementations as <see cref="IAgentCardContributor"/> services;
/// the core registers none. They run in registration order, once per card (public and extended) before its bytes and ETag are
/// cached, so the ETag covers what they add. A contributor may run more than once for the same card when requests race; it must
/// give the same result each time.
/// </summary>
/// <remarks>
/// A contributor may only add: new top-level members; entries appended to <c>supportedInterfaces</c> and
/// <c>capabilities.extensions</c>; new keys in <c>securitySchemes</c>; and <c>securityRequirements</c> (on the card or a skill)
/// where none is set. It must not change or remove anything already there. Debug builds assert this after each contributor.
/// The extended card that JSON-RPC and REST serve carries every added member; gRPC carries only what its card message can hold.
/// </remarks>
public interface IAgentCardContributor
{
    /// <summary>Adds members to <paramref name="cardJson"/>, the card as it will be served.</summary>
    /// <param name="card">Which card this is.</param>
    /// <param name="cardJson">The card JSON, including what earlier contributors added. Change it in place.</param>
    void Contribute(AgentCardContext card, JsonObject cardJson);
}

/// <summary>The card an <see cref="IAgentCardContributor"/> is asked to contribute to.</summary>
/// <param name="AgentName">Catalog name of the agent (the <c>{agent}</c> route value).</param>
/// <param name="Card">The card model before any contributor ran.</param>
/// <param name="IsExtended">True for the authenticated extended card, false for the public card.</param>
public sealed record AgentCardContext(string AgentName, AgentCard Card, bool IsExtended);
