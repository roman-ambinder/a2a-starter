using Ambinder.A2A.AspNetCore.Transports;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Auth;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Server.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.AspNetCore.Hosting;

/// <summary>
/// Runs the agent's policy (<see cref="IAgentAuthorizer"/>), then takes the caller from <see cref="IOwnerResolver"/>.
/// The tenant follows <see cref="TransportRequest.ResolveTenant"/>: an empty request tenant is filled with the agent's interface
/// tenant, and a different one is rejected with <c>InvalidParams</c>. Without an interface tenant it is the request's tenant, or empty.
/// With <see cref="A2ASecurityOptions.KeepCallerToken"/> the caller carries the request's bearer token.
/// </summary>
internal sealed class DefaultA2ACallerResolver(AgentCatalog catalog, IAgentAuthorizer authorizer, IOwnerResolver owners,
    IOptions<A2AServerOptions> options, IOptions<A2ASecurityOptions> security) : IA2ACallerResolver
{
    private const string BearerPrefix = "Bearer ";

    public async ValueTask<(A2ACaller Caller, string Tenant)> ResolveAsync(HttpContext http, string agentName, string? requestTenant,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!catalog.TryGet(agentName, out var agent))
            throw new ArgumentException($"Unknown agent '{agentName}'.", nameof(agentName));
        await authorizer.AuthorizeAsync(agent, http.User, ct).ConfigureAwait(false);
        var tenant = TransportRequest.ResolveTenant(agentName, requestTenant, options.Value) ?? "";
        var caller = await owners.ResolveAsync(http.User, agent, ct).ConfigureAwait(false);
        if (security.Value.KeepCallerToken && BearerToken(http) is { } token)
            caller = caller with { AccessToken = token };
        return (caller, tenant);
    }

    /// <summary>The token of an <c>Authorization: Bearer</c> header, or null.</summary>
    private static string? BearerToken(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var token = header[BearerPrefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }
}
