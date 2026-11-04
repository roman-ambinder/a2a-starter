namespace Ambinder.A2A.AspNetCore.Hosting;

/// <summary>
/// The agents that run as anonymous only because the host is in Development, no authentication scheme is registered and the
/// agent has no explicit access mode. Filled once by the startup check, before the server listens; read per request.
/// </summary>
internal sealed class DevelopmentAnonymousAgents
{
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <summary>Adds an agent. Called by the startup check only.</summary>
    public void Add(string agentName) => _names.Add(agentName);

    /// <summary>True when the agent runs anonymously by the Development rule.</summary>
    public bool Contains(string agentName) => _names.Contains(agentName);
}
