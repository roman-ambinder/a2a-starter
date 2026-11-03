using A2A.Protocol;
using A2A.Server.Agents;
using Microsoft.AspNetCore.Http;

namespace A2A.Server.Hosting;

/// <summary>
/// Serves JSON-RPC and REST requests that ask for an A2A version the core does not support (for example A2A 0.3).
/// Register implementations as <see cref="IA2AVersionDispatcher"/> services; the core registers none. The HTTP transports
/// ask each one, in registration order, after the agent is resolved and authorized and before the body is read. The first
/// that claims the version handles the whole request and the core writes nothing more. gRPC does not use dispatchers.
/// </summary>
public interface IA2AVersionDispatcher
{
    /// <summary>
    /// True if this dispatcher serves requests that ask for <paramref name="version"/>: the normalized <c>Major.Minor</c>
    /// (<c>"0.3"</c> for an empty header), or the trimmed raw value when it does not parse.
    /// </summary>
    /// <param name="version">The requested version, normalized as described above.</param>
    bool CanHandle(string version);

    /// <summary>Handles the whole HTTP request (JSON-RPC or REST) for the agent, including writing the response.</summary>
    /// <remarks>
    /// The dispatcher owns its errors: it should write its own error response rather than throw. A thrown exception gives a bare
    /// 500 on JSON-RPC. The request body is not size-limited before the dispatcher runs, so the dispatcher must enforce its own limit.
    /// </remarks>
    /// <param name="http">The request; its body has not been read.</param>
    /// <param name="agent">The resolved agent.</param>
    /// <param name="version">The requested version, as passed to <see cref="CanHandle"/>.</param>
    /// <param name="ct">Cancelled when the client goes away.</param>
    Task HandleAsync(HttpContext http, A2AAgentRoute agent, string version, CancellationToken ct);
}

/// <summary>The agent a request was routed to, as given to an <see cref="IA2AVersionDispatcher"/>.</summary>
/// <param name="Name">Catalog name of the agent (the <c>{agent}</c> route value).</param>
/// <param name="Registration">The agent's registration.</param>
/// <param name="Card">The agent's public card, with its interfaces filled in.</param>
public sealed record A2AAgentRoute(string Name, AgentRegistration Registration, AgentCard Card);
