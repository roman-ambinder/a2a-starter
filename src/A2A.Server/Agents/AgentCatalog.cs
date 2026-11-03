using System.Diagnostics.CodeAnalysis;

namespace A2A.Server.Agents;

/// <summary>The hosted agents, by name (case-sensitive).</summary>
public sealed class AgentCatalog
{
    private readonly Dictionary<string, AgentRegistration> _agents = new(StringComparer.Ordinal);

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

    public IReadOnlyCollection<AgentRegistration> All => _agents.Values;

    public bool TryGet(string name, [MaybeNullWhen(false)] out AgentRegistration registration)
        => _agents.TryGetValue(name, out registration);

    /// <summary>Returns the registration. Throws <see cref="KeyNotFoundException"/> for an unknown name (transports map that to HTTP 404).</summary>
    public AgentRegistration Get(string name)
        => TryGet(name, out var r) ? r : throw new KeyNotFoundException($"Unknown agent '{name}'.");
}
