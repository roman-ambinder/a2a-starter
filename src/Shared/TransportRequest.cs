using System.Diagnostics;
using System.Security.Claims;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Security;
using Ambinder.A2A.AspNetCore.Hosting;
using Ambinder.A2A.Server.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.AspNetCore.Transports;

/// <summary>
/// Per-request steps shared by the transports: auth failure, extended card check, call context, tenant. Compiled into
/// <c>Ambinder.A2A.AspNetCore</c> and <c>Ambinder.A2A.AspNetCore.Grpc</c>; the HTTP transports add version dispatch in a partial part.
/// </summary>
internal static partial class TransportRequest
{
    /// <summary>
    /// True when the primary identity (<see cref="ClaimsPrincipal.Identity"/>) is authenticated. The same rule as
    /// <see cref="DefaultOwnerResolver.ResolveAsync"/>, so a caller is never authorized and then treated as anonymous for ownership.
    /// </summary>
    public static bool IsAuthenticated(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true;

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
    public static void AuthorizeExtendedCard(AgentRegistration agent, A2ACaller caller)
    {
        if (agent.PublicCard.Capabilities.ExtendedAgentCard != true || IsAuthenticated(caller.Principal)) return;
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

    public static A2ACallContext CreateCallContext(HttpContext http, AgentRegistration agent, A2ACaller caller, string? version,
        IReadOnlyList<string> extensions, object request, string transport)
        => new()
        {
            AgentName = agent.Name,
            Caller = caller,
            Tenant = ResolveTenant(agent.Name, TenantOf(request), http.RequestServices.GetRequiredService<IOptions<A2AServerOptions>>().Value) ?? "",
            ProtocolVersion = ServiceParameters.NormalizeVersion(version),
            RequestedExtensions = extensions,
            Transport = transport,
        };

    /// <summary>
    /// Resolves the caller and tenant through the registered <see cref="IA2ACallerResolver"/> (agent policy, owner, tenant). An
    /// <see cref="A2AAuthException"/> is tagged on the request activity (<c>error.type</c> = <c>401</c> / <c>403</c>) and rethrown.
    /// </summary>
    public static async Task<(A2ACaller Caller, string Tenant)> ResolveCallerAsync(HttpContext http, AgentRegistration agent,
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
