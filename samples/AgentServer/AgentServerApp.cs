using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Hosting;
using AgentServer.Agents;
using AgentServer.Auth;
using AgentServer.Cards;
using AgentServer.Llm;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SampleTelemetry;

namespace AgentServer;

/// <summary>
/// The demo agent server: four agents (echo, report, booking, secure) over JSON-RPC, REST and gRPC, with Entra-shaped bearer
/// tokens and API keys. Configuration sections: <c>A2A</c> (<see cref="A2AServerOptions"/>), <c>Auth</c> (<see cref="AuthOptions"/>),
/// <c>FakeAgents</c> (<see cref="FakeAgentOptions"/>), <c>Llm</c> (<see cref="ChatClientRegistration"/>).
/// </summary>
public static class AgentServerApp
{
    public const string A2ASectionName = "A2A";

    /// <summary>Content root (appsettings*.json) below the output folder; see AgentServer.csproj.</summary>
    public static string ContentRoot => Path.Combine(AppContext.BaseDirectory, "agentserver-config");

    /// <summary>
    /// Builds the app. <paramref name="configure"/> runs right after the builder is created, before any configuration is read,
    /// so it can add or override configuration. Throws at build time for an unsupported <c>Llm:Provider</c> or missing
    /// <c>Auth:Authority</c>/<c>Auth:Audience</c>; A2A option errors (unknown policy, default agent or gRPC key) fail at start.
    /// </summary>
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = ContentRoot });
        configure?.Invoke(builder);
        builder.AddSampleTelemetry("agentserver", SampleTelemetryExtensions.LlmSource);
        var config = builder.Configuration;

        builder.Services.Configure<FakeAgentOptions>(config.GetSection(FakeAgentOptions.SectionName));
        builder.Services.AddDemoChatClient(config);
        var auth = builder.Services.AddA2AAuth(config);

        builder.ConfigureA2AKestrel();
        var a2a = builder.Services.AddA2AServer(o => config.GetSection(A2ASectionName).Bind(o));
        foreach (var name in AgentCards.Names)
        {
            Func<IServiceProvider, AgentCard> publicCard = sp => AgentCards.Build(name, Server(sp), auth, extended: false);
            Func<IServiceProvider, AgentCard>? extendedCard =
                AgentCards.HasExtendedCard(name) ? sp => AgentCards.Build(name, Server(sp), auth, extended: true) : null;
            var policy = AgentCards.PolicyFor(name);
            switch (name)
            {
                case AgentCards.Echo: a2a.AddAgent<EchoAgent>(name, publicCard, extendedCard, policy); break;
                case AgentCards.Report: a2a.AddAgent<ReportAgent>(name, publicCard, extendedCard, policy); break;
                case AgentCards.Booking: a2a.AddAgent<BookingAgent>(name, publicCard, extendedCard, policy); break;
                case AgentCards.Secure: a2a.AddAgent<SecureAgent>(name, publicCard, extendedCard, policy); break;
                default: throw new InvalidOperationException($"No handler for agent '{name}'.");
            }
        }

        builder.Services.AddHealthChecks();

        var app = builder.Build();
        app.UseAuthentication();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready");
        app.MapA2A();
        app.MapA2AGrpc();
        return app;
    }

    private static A2AServerOptions Server(IServiceProvider services) => services.GetRequiredService<IOptions<A2AServerOptions>>().Value;
}
