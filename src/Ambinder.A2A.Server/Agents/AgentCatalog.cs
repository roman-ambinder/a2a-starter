using System.Diagnostics.CodeAnalysis;

namespace Ambinder.A2A.Server.Agents;

/// <summary>The hosted agents, by name (case-sensitive).</summary>
/// <remarks>The catalog is built once at startup and does not change. It is safe to use from many threads.</remarks>
public sealed class AgentCatalog
{
    private readonly Dictionary<string, AgentRegistration> _agents = new(StringComparer.Ordinal);

    /// <summary>Creates a catalog.</summary>
    /// <param name="registrations">The agents. Each name must be unique, and each handler type must implement <see cref="IAgentHandler"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registrations"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A handler type does not implement <see cref="IAgentHandler"/>, or a name is used twice.</exception>
    public AgentCatalog(IEnumerable<AgentRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        foreach (var r in registrations)
        {
            if (!typeof(IAgentHandler).IsAssignableFrom(r.HandlerType))
                throw new ArgumentException($"Agent '{r.Name}': {r.HandlerType} does not implement {nameof(IAgentHandler)}.", nameof(registrations));
            if (!_agents.TryAdd(r.Name, r))
                throw new ArgumentException($"Agent '{r.Name}' is registered twice.", nameof(registrations));
        }
    }

    /// <summary>All registered agents.</summary>
    public IReadOnlyCollection<AgentRegistration> All => _agents.Values;

    /// <summary>Looks up an agent by name.</summary>
    /// <param name="name">The agent name. Names are case-sensitive.</param>
    /// <param name="registration">The registration, when the agent exists.</param>
    /// <returns><see langword="true"/> when the agent exists.</returns>
    public bool TryGet(string name, [MaybeNullWhen(false)] out AgentRegistration registration)
        => _agents.TryGetValue(name, out registration);

    /// <summary>Returns the registration. Throws <see cref="KeyNotFoundException"/> for an unknown name (transports map that to HTTP 404).</summary>
    /// <param name="name">The agent name. Names are case-sensitive.</param>
    /// <returns>The registration.</returns>
    /// <exception cref="KeyNotFoundException">No agent has this name.</exception>
    public AgentRegistration Get(string name)
        => TryGet(name, out var r) ? r : throw new KeyNotFoundException($"Unknown agent '{name}'.");
}
