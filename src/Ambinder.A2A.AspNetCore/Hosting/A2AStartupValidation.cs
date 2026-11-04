using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.AspNetCore.Hosting;

/// <summary>
/// Fails host startup (before the server listens) when the A2A setup is inconsistent: an agent's authorization policy is not
/// registered, <see cref="A2AServerOptions.DefaultAgent"/> is not an agent, or a <see cref="A2AServerOptions.GrpcPorts"/> key is
/// not an agent, or an agent needs authentication while no authentication scheme is registered (in the Development environment an
/// agent with no explicit access mode is exempt: it runs anonymously and startup logs a warning for it). Building the <see cref="AgentCatalog"/> here also runs the card factories once, so their errors surface early.
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
                $"A2AServerOptions.GrpcPorts has entries for unknown agents: {string.Join(", ", unknownPorts.Select(k => $"'{k}'"))}. " +
                "Remove those entries, or add the agents with AddAgent so every GrpcPorts key is the name of a registered agent.");

        var policies = services.GetRequiredService<IAuthorizationPolicyProvider>();
        foreach (var agent in catalog.All)
        {
            if (agent.AuthorizationPolicy is not { } name) continue;
            // Synchronous startup check; the default policy provider completes synchronously. Package 09 moves startup checks.
#pragma warning disable RS0030
            var policy = policies.GetPolicyAsync(name).GetAwaiter().GetResult();
#pragma warning restore RS0030
            if (policy is null)
                throw new InvalidOperationException(
                    $"Agent '{agent.Name}' uses authorization policy '{name}', which is not registered. Add it with AddAuthorization/AddAuthorizationBuilder.");
        }

        ValidateAccess(services, catalog);
    }

    private static void ValidateAccess(IServiceProvider services, AgentCatalog catalog)
    {
        // Synchronous startup check; the default scheme provider completes synchronously. Package 09 moves startup checks.
        var provider = services.GetService<IAuthenticationSchemeProvider>();
#pragma warning disable RS0030
        var anyScheme = provider is not null && provider.GetAllSchemesAsync().GetAwaiter().GetResult().Any();
#pragma warning restore RS0030
        if (anyScheme) return;

        var development = services.GetRequiredService<IHostEnvironment>().IsDevelopment();
        var openByDevelopment = services.GetRequiredService<DevelopmentAnonymousAgents>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(A2AStartupValidation).FullName!);
        foreach (var agent in catalog.All)
        {
            if (agent.Access == AgentAccess.Anonymous) continue;
            if (agent.Access is null && development)
            {
                openByDevelopment.Add(agent.Name);
                HostingLog.AgentAnonymousInDevelopment(logger, agent.Name);
                continue;
            }

            throw new InvalidOperationException(
                $"Agent '{agent.Name}' requires authentication but no scheme is registered. Call AddEntraAuthentication(...) or mark the agent AllowAnonymous().");
        }
    }
}
