using A2A.Protocol;
using Microsoft.AspNetCore.Http;

namespace A2A.Server.Hosting;

/// <summary>
/// Serves A2A requests for a protocol version the core does not support, for example 0.3 (added by a separate compat package).
/// The core registers none.
/// </summary>
/// <remarks>
/// The JSON-RPC and REST endpoints call it after they resolve the agent, authenticate the caller and resolve the tenant, and
/// before they read the JSON-RPC body or resolve the REST operation. Dispatchers are asked in registration order; the first
/// that claims the version handles the whole request and the core writes nothing more. Register implementations as singletons;
/// calls can run concurrently. gRPC does not use dispatchers. After 1.0, new members get a default implementation.
/// </remarks>
internal interface IA2AVersionDispatcher
{
    /// <summary>Says whether this dispatcher serves requests that ask for the given protocol version.</summary>
    /// <param name="version">The requested version as <c>Major.Minor</c> (<c>"0.3"</c> when the request has no version header), or the trimmed raw value when it does not parse.</param>
    /// <returns><see langword="true"/> if <see cref="HandleAsync"/> should handle the request.</returns>
    bool CanHandle(string version);

    /// <summary>Handles the whole HTTP request (JSON-RPC or REST) for the agent and writes the response.</summary>
    /// <remarks>
    /// The dispatcher owns its errors: it should write its own error response rather than throw. A thrown exception gives a bare
    /// 500 on JSON-RPC. The request body is not size-limited before the dispatcher runs, so the dispatcher must enforce its own limit.
    /// </remarks>
    /// <param name="http">The request. Its body has not been read.</param>
    /// <param name="context">The agent, binding, caller and tenant the core resolved for this request.</param>
    /// <param name="ct">Cancelled when the client goes away.</param>
    /// <returns>A task that completes when the response is written.</returns>
    Task HandleAsync(HttpContext http, A2AVersionDispatchContext context, CancellationToken ct);
}

/// <summary>What the core resolved for a request that an <see cref="IA2AVersionDispatcher"/> handles.</summary>
internal sealed class A2AVersionDispatchContext
{
    /// <summary>The agent's registered name, as used in its URLs.</summary>
    public required string AgentName { get; init; }

    /// <summary>The agent's public card.</summary>
    public required AgentCard PublicCard { get; init; }

    /// <summary>The binding the request came in on: <c>"JSONRPC"</c> or <c>"HTTP+JSON"</c>.</summary>
    public required string Binding { get; init; }

    /// <summary>The raw <c>A2A-Version</c> value; empty when the request had none.</summary>
    public required string RequestedVersion { get; init; }

    /// <summary>The authenticated caller, with the same owner rules as 1.0 requests.</summary>
    public required DispatchCaller Caller { get; init; }

    /// <summary>
    /// The resolved tenant; empty when the agent has none. For REST on a version the core does not serve, a tenant prefix is read
    /// only from a path of the form <c>{tenant}/v1/...</c> (first segment not <c>v1</c>, second segment <c>v1</c>); other paths give an empty tenant.
    /// </summary>
    public required string Tenant { get; init; }

    /// <summary>
    /// For REST, the path after the agent's REST base (for example <c>/v1/tasks/t1</c>), without a tenant prefix of the form
    /// <c>{tenant}/v1/...</c> (that prefix is in <see cref="Tenant"/>); other paths are unchanged. Empty for JSON-RPC.
    /// </summary>
    public required PathString RestPath { get; init; }
}
