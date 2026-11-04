using Ambinder.A2A.AspNetCore.Auth;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Auth;
using Ambinder.A2A.Server.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.AspNetCore.Transports;

// The gRPC part of TransportRequest (the shared part is src/Shared/TransportRequest.cs). The HTTP transports resolve the caller
// through IA2ACallerResolver, which is internal to Ambinder.A2A.AspNetCore (no InternalsVisibleTo between src assemblies), so this
// package applies the same default rules itself: agent policy, then the owner from IOwnerResolver, then the tenant rule.
internal static partial class TransportRequest
{
    /// <summary>
    /// Runs the agent's policy (<see cref="IAgentAuthorizer"/>), takes the owner from <see cref="IOwnerResolver"/> and resolves the
    /// tenant with <see cref="ResolveTenant"/>. An <see cref="A2AAuthException"/> is tagged on the request activity
    /// (<c>error.type</c> = <c>401</c> / <c>403</c>) and rethrown.
    /// </summary>
    public static async Task<(DispatchCaller Caller, string Tenant)> ResolveCallerAsync(HttpContext http, AgentRegistration agent,
        string? requestTenant)
    {
        try
        {
            var authorizer = http.RequestServices.GetRequiredService<IAgentAuthorizer>();
            await authorizer.AuthorizeAsync(agent, http.User, http.RequestAborted).ConfigureAwait(false);
        }
        catch (A2AAuthException ex)
        {
            TagError(ex.HttpStatus.ToString(System.Globalization.CultureInfo.InvariantCulture));
            throw;
        }
        var owners = http.RequestServices.GetService<IOwnerResolver>() ?? DefaultOwnerResolver.Instance;
        var options = http.RequestServices.GetRequiredService<IOptions<A2AServerOptions>>().Value;
        var tenant = ResolveTenant(agent.Name, requestTenant, options) ?? "";
        return (new DispatchCaller(http.User, owners.GetOwner(http.User)), tenant);
    }
}
