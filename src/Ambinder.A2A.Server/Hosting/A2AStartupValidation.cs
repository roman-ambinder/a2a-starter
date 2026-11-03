using Ambinder.A2A.Server.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Server.Hosting;

/// <summary>
/// Fails host startup (before the server listens) when the A2A setup is inconsistent: an agent's authorization policy is not
/// registered, <see cref="A2AServerOptions.DefaultAgent"/> is not an agent, or a <see cref="A2AServerOptions.GrpcPorts"/> key is
/// not an agent. Building the <see cref="AgentCatalog"/> here also runs the card factories once, so their errors surface early.
/// </summary>
internal sealed class A2AStartupValidation : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        Validate(app.ApplicationServices);
        next(app);
    };

    internal static void Validate(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<A2AServerOptions>>().Value;
        var catalog = services.GetRequiredService<AgentCatalog>(); // throws for an unknown DefaultAgent

        var unknownPorts = options.GrpcPorts.Keys.Where(k => !catalog.TryGet(k, out _)).ToList();
        if (unknownPorts.Count > 0)
            throw new InvalidOperationException(
                $"A2AServerOptions.GrpcPorts has entries for unknown agents: {string.Join(", ", unknownPorts.Select(k => $"'{k}'"))}.");

        var policies = services.GetRequiredService<IAuthorizationPolicyProvider>();
        foreach (var agent in catalog.All)
        {
            if (agent.AuthorizationPolicy is not { } name) continue;
            if (policies.GetPolicyAsync(name).GetAwaiter().GetResult() is null)
                throw new InvalidOperationException(
                    $"Agent '{agent.Name}' uses authorization policy '{name}', which is not registered. Add it with AddAuthorization/AddAuthorizationBuilder.");
        }
    }
}
