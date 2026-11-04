using Ambinder.A2A.AspNetCore.Auth;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.AspNetCore.Transports;

/// <summary>Per-request steps shared by the HTTP transports: authorization, owner, call context.</summary>
internal static class TransportRequest
{
    /// <summary>
    /// True when the primary identity (<see cref="System.Security.Claims.ClaimsPrincipal.Identity"/>) is authenticated. The same rule as
    /// <see cref="DefaultOwnerResolver.GetOwner"/>, so a caller is never authorized and then treated as anonymous for ownership.
    /// </summary>
    public static bool IsAuthenticated(System.Security.Claims.ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true;

    /// <summary>
    /// Runs the agent's policy (<see cref="IAgentAuthorizer"/>), then, when <paramref name="requireAuthenticated"/>,
    /// demands an authenticated caller. Returns the owner key. Throws <see cref="A2AAuthException"/>.
    /// </summary>
    public static async Task<string> AuthorizeAsync(HttpContext http, AgentRegistration agent, bool requireAuthenticated)
    {
        var authorizer = http.RequestServices.GetRequiredService<IAgentAuthorizer>();
        await authorizer.AuthorizeAsync(agent, http.User, http.RequestAborted).ConfigureAwait(false);
        if (requireAuthenticated && !IsAuthenticated(http.User))
            throw new A2AAuthException(401, "Authentication is required.");
        var owners = http.RequestServices.GetService<IOwnerResolver>() ?? DefaultOwnerResolver.Instance;
        return owners.GetOwner(http.User);
    }

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

    public static A2ACallContext CreateCallContext(HttpContext http, AgentRegistration agent, string owner, string? version,
        IReadOnlyList<string> extensions, object request, string transport)
        => new(agent.Name, http.User, owner, TenantOf(request), version, extensions) { Transport = transport };

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
