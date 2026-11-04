using Ambinder.A2A.AspNetCore.Hosting;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.AspNetCore.Transports;

// The HTTP part of TransportRequest (the shared part is src/Shared/TransportRequest.cs): version dispatch.
internal static partial class TransportRequest
{
    /// <summary>
    /// Steps shared by the JSON-RPC and REST endpoints once the agent is known: read the requested version, resolve the caller and
    /// tenant (an auth failure ends the request here, the same for every version), then, when the version is not one the core
    /// serves, ask the registered <see cref="IA2AVersionDispatcher"/> instances in order. The first that claims it handles the
    /// whole request and <c>Handled</c> is true; the caller then writes nothing more. Otherwise <c>Handled</c> is false and the
    /// caller goes on; its own version check then rejects an unsupported version. Call it before reading the body or resolving
    /// the operation.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="agent">The agent the request addresses.</param>
    /// <param name="binding">The binding the request came in on.</param>
    /// <param name="restPath">For REST, the path after the REST base; empty otherwise.</param>
    /// <param name="requestTenant">Computes the tenant the request names, given whether the version is supported (null: none).</param>
    public static async Task<(bool Handled, A2ACaller Caller)> TryDispatchVersionAsync(HttpContext http, AgentRegistration agent,
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
}
