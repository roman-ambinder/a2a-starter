using Ambinder.A2A.Integration.Tests.Fixtures;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Server.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ambinder.A2A.Integration.Tests.Hosting;

/// <summary>A2A configuration mistakes stop the host at startup, before it listens.</summary>
public sealed class StartupValidationTests
{
    private static async Task<InvalidOperationException> StartFails(Action<A2AServerOptions> options, string? policy = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthorizationBuilder().AddPolicy("Known", p => p.RequireAuthenticatedUser());
        builder.Services.AddA2AServer(o =>
            {
                o.PublicBaseUrl = "http://127.0.0.1:1";
                options(o);
            })
            .AddAgent<EchoAgent>("a", _ => MinimalServer.Card("a", extended: false), authorizationPolicy: policy);
        await using var app = builder.Build();
        app.MapA2A();
        return await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unknown_authorization_policy_fails_startup()
        => Assert.Contains("'Missing'", (await StartFails(_ => { }, policy: "Missing")).Message);

    [Fact]
    public async Task Unknown_default_agent_fails_startup()
        => Assert.Contains("'nope'", (await StartFails(o => o.DefaultAgent = "nope")).Message);

    [Fact]
    public async Task Grpc_port_for_unknown_agent_fails_startup()
        => Assert.Contains("'ghost'", (await StartFails(o => o.GrpcPorts["ghost"] = 1)).Message);

    [Fact]
    public async Task Valid_configuration_starts()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthorizationBuilder().AddPolicy("Known", p => p.RequireAuthenticatedUser());
        builder.Services.AddA2AServer(o => { o.PublicBaseUrl = "http://127.0.0.1:1"; o.DefaultAgent = "a"; })
            .AddAgent<EchoAgent>("a", _ => MinimalServer.Card("a", extended: false), authorizationPolicy: "Known", access: AgentAccess.Anonymous);
        await using var app = builder.Build();
        app.MapA2A();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }
}
