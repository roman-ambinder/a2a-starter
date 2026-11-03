using System.Diagnostics;
using A2A.Protocol;
using A2A.Server.Agents;
using A2A.Server.Auth;
using A2A.Server.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace A2A.Server.Transports;

/// <summary>Per-request steps shared by the transports: caller, version dispatch, call context.</summary>
internal static class TransportRequest
{
    /// <summary>
    /// Resolves the caller and tenant through <see cref="IA2ACallerResolver"/> (agent policy, owner). An
    /// <see cref="A2AAuthException"/> is tagged on the request activity (<c>error.type</c> = <c>401</c> / <c>403</c>) and rethrown.
    /// </summary>
    public static async Task<(DispatchCaller Caller, string Tenant)> ResolveCallerAsync(HttpContext http, AgentRegistration agent,
        string? requestTenant)
    {
        var resolver = http.RequestServices.GetRequiredService<IA2ACallerResolver>();
        try
        {
            return await resolver.ResolveAsync(http, agent.Name, requestTenant, http.RequestAborted).ConfigureAwait(false);
        }
        catch (A2AAuthException ex)
        {
            TagError(ex.HttpStatus.ToString(System.Globalization.CultureInfo.InvariantCulture));
            throw;
        }
    }

    /// <summary>
    /// Steps shared by the JSON-RPC and REST endpoints once the agent is known: read the requested version, resolve the caller and
    /// tenant (an auth failure ends the request here, the same for every version), then, when the version is not one the core
    /// serves, ask the registered <see cref="IA2AVersionDispatcher"/> instances in order. The first that claims it handles the
    /// whole request and <c>Handled</c> is true; the caller then writes nothing more. Otherwise <c>Handled</c> is false and the
    /// caller goes on; its own version check then rejects an unsupported version. Call it before reading the body or resolving
    /// the operation.
    /// </summary>
    /// <param name="binding">The binding the request came in on.</param>
    /// <param name="restPath">For REST, the path after the REST base; empty otherwise.</param>
    /// <param name="requestTenant">Computes the tenant the request names, given whether the version is supported (null: none).</param>
    public static async Task<(bool Handled, DispatchCaller Caller)> TryDispatchVersionAsync(HttpContext http, AgentRegistration agent,
        string binding, PathString restPath, Func<bool, string?>? requestTenant = null)
    {
        var (raw, _) = ServiceParameters.Read(http);
        var supported = ServiceParameters.IsSupportedVersion(raw);
        var (caller, tenant) = await ResolveCallerAsync(http, agent, requestTenant?.Invoke(supported)).ConfigureAwait(false);
        if (supported) return (false, caller);

        var version = ServiceParameters.NormalizeVersion(raw);
        foreach (var dispatcher in http.RequestServices.GetServices<IA2AVersionDispatcher>())
        {
            if (!dispatcher.CanHandle(version)) continue;
            var context = new A2AVersionDispatchContext
            {
                AgentName = agent.Name,
                PublicCard = agent.PublicCard,
                Binding = binding,
                RequestedVersion = raw ?? "",
                Caller = caller,
                Tenant = tenant,
                RestPath = restPath,
            };
            await dispatcher.HandleAsync(http, context, http.RequestAborted).ConfigureAwait(false);
            return (true, caller);
        }
        return (false, caller);
    }

    /// <summary>
    /// <see cref="ServiceParameters.EnsureSupportedVersion"/>, with the rejection tagged on the request activity
    /// (<c>error.type</c> = <c>VERSION_NOT_SUPPORTED</c>) before it is rethrown.
    /// </summary>
    public static void EnsureVersion(string? version)
    {
        try
        {
            ServiceParameters.EnsureSupportedVersion(version);
        }
        catch (A2AException ex)
        {
            TagError(A2AError.Get(ex.Code).Reason ?? ex.Code.ToString());
            throw;
        }
    }

    /// <summary>
    /// The extra check for GetExtendedAgentCard: an authenticated caller, but only when the card declares
    /// <c>capabilities.extendedAgentCard</c>. Otherwise the handler answers UnsupportedOperation to anyone (spec 3.3.4).
    /// Throws <see cref="A2AAuthException"/> (401).
    /// </summary>
    public static void AuthorizeExtendedCard(AgentRegistration agent, DispatchCaller caller)
    {
        if (agent.PublicCard.Capabilities.ExtendedAgentCard != true || AgentAuthorizer.IsAuthenticated(caller.User)) return;
        TagError("401");
        throw new A2AAuthException(401, "Authentication is required.");
    }

    private static void TagError(string type) => Activity.Current?.SetTag("error.type", type);

    /// <summary>
    /// HTTP 401/403 with no body. Uses the app's default challenge/forbid scheme when there is one (so for example
    /// <c>WWW-Authenticate</c> is set), else only the status code.
    /// </summary>
    public static async Task WriteAuthFailureAsync(HttpContext http, A2AAuthException ex)
    {
        var schemes = http.RequestServices.GetService<IAuthenticationSchemeProvider>();
        if (ex.HttpStatus == StatusCodes.Status403Forbidden)
        {
            if (schemes is not null && await schemes.GetDefaultForbidSchemeAsync().ConfigureAwait(false) is not null)
                await http.ForbidAsync().ConfigureAwait(false);
            if (!http.Response.HasStarted) http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (schemes is not null && await schemes.GetDefaultChallengeSchemeAsync().ConfigureAwait(false) is not null)
            await http.ChallengeAsync().ConfigureAwait(false);
        if (!http.Response.HasStarted) http.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    public static A2ACallContext CreateCallContext(HttpContext http, AgentRegistration agent, DispatchCaller caller, string? version,
        IReadOnlyList<string> extensions, object request, string transport)
        => new(agent.Name, caller.User, caller.Owner,
            ResolveTenant(agent.Name, TenantOf(request), http.RequestServices.GetRequiredService<IOptions<A2AServerOptions>>().Value),
            ServiceParameters.NormalizeVersion(version), extensions)
        { Transport = transport };

    /// <summary>
    /// The tenant of a request to <paramref name="agentName"/> (spec 8.3.2): the interface's tenant from
    /// <see cref="A2AServerOptions.InterfaceTenants"/>, which an empty request tenant is filled with. Without an interface tenant
    /// the request value is kept.
    /// </summary>
    /// <exception cref="A2AException"><c>InvalidParams</c>: the request names a different tenant than the interface.</exception>
    public static string? ResolveTenant(string agentName, string? requestTenant, A2AServerOptions options)
    {
        if (!options.InterfaceTenants.TryGetValue(agentName, out var interfaceTenant) || string.IsNullOrEmpty(interfaceTenant))
            return requestTenant;
        if (string.IsNullOrEmpty(requestTenant) || requestTenant == interfaceTenant) return interfaceTenant;
        throw new A2AException(A2AErrorCode.InvalidParams,
            $"tenant '{requestTenant}' does not match this interface (tenant '{interfaceTenant}'). Leave tenant empty or send '{interfaceTenant}'.");
    }

    /// <summary>The <c>tenant</c> field of a request record, or null.</summary>
    public static string? TenantOf(object request) => request switch
    {
        SendMessageRequest r => r.Tenant,
        GetTaskRequest r => r.Tenant,
        ListTasksRequest r => r.Tenant,
        CancelTaskRequest r => r.Tenant,
        SubscribeToTaskRequest r => r.Tenant,
        TaskPushNotificationConfig r => r.Tenant,
        GetTaskPushNotificationConfigRequest r => r.Tenant,
        ListTaskPushNotificationConfigsRequest r => r.Tenant,
        DeleteTaskPushNotificationConfigRequest r => r.Tenant,
        GetExtendedAgentCardRequest r => r.Tenant,
        _ => null,
    };
}
