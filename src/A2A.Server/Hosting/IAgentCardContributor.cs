using System.Text.Json.Nodes;
using A2A.Protocol;

namespace A2A.Server.Hosting;

/// <summary>What an <see cref="IAgentCardContributor"/> is told about the card it edits.</summary>
public sealed record AgentCardContext
{
    /// <summary>The agent's registered name.</summary>
    public required string AgentName { get; init; }

    /// <summary>The card model before any contributor ran.</summary>
    public required AgentCard Card { get; init; }

    /// <summary><see langword="true"/> for the extended card. Always <see langword="false"/> in this version; see the remarks on <see cref="IAgentCardContributor"/>.</summary>
    public bool IsExtended { get; init; }
}

/// <summary>Adds members to an agent card's JSON before the card is cached and served.</summary>
/// <remarks>
/// Contributors run once per card, in registration order, on the public card endpoints only. A contributor may add new
/// top-level members, append to <c>supportedInterfaces</c> and <c>capabilities.extensions</c>, add new <c>securitySchemes</c>
/// keys, and set <c>securityRequirements</c> where the card has none. It must not change or remove anything already set.
/// Register implementations as singletons; calls for different agents can run concurrently.
/// </remarks>
public interface IAgentCardContributor
{
    /// <summary>Adds this contributor's members to the card JSON.</summary>
    /// <param name="card">The agent and card being served.</param>
    /// <param name="cardJson">The card JSON to add to. Changes are kept.</param>
    void Contribute(AgentCardContext card, JsonObject cardJson);
}
