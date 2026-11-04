using Ambinder.A2A.AspNetCore.Hosting;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.AspNetCore.Tests.Hosting;

/// <summary>Startup checks of the A2A setup say what to change (principle 3).</summary>
public class StartupValidationMessageTests
{
    private static ServiceProvider Provider(Action<Ambinder.A2A.Server.Hosting.A2AServerOptions> configure, string? policy = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddA2AServer(configure)
            .AddAgent<EchoAgent>("echo", _ => new AgentCard { Name = "echo" }, authorizationPolicy: policy);
        return services.BuildServiceProvider();
    }

    private sealed class EchoAgent : Ambinder.A2A.Server.IAgentHandler
    {
        public ValueTask ExecuteAsync(Ambinder.A2A.Server.AgentRequestContext context, Ambinder.A2A.Server.IAgentEventSink sink, CancellationToken ct)
            => ValueTask.CompletedTask;
    }

    [Fact]
    public void GrpcPorts_for_an_unknown_agent_says_to_remove_the_entry_or_add_the_agent()
    {
        using var provider = Provider(o =>
        {
            o.PublicBaseUrl = "http://localhost:5200";
            o.GrpcPorts["ghost"] = 5201;
        });

        var ex = Assert.Throws<InvalidOperationException>(() => A2AStartupValidation.Validate(provider));

        Assert.Contains("A2AServerOptions.GrpcPorts", ex.Message);
        Assert.Contains("'ghost'", ex.Message);
        Assert.Contains("Remove those entries, or add the agents with AddAgent", ex.Message);
    }

    [Fact]
    public void Unknown_authorization_policy_says_to_add_it()
    {
        using var provider = Provider(o => o.PublicBaseUrl = "http://localhost:5200", policy: "NoSuchPolicy");

        var ex = Assert.Throws<InvalidOperationException>(() => A2AStartupValidation.Validate(provider));

        Assert.Contains("'NoSuchPolicy'", ex.Message);
        Assert.Contains("Add it with AddAuthorization", ex.Message);
    }

    [Fact]
    public void Kestrel_port_clash_names_both_ports_and_says_what_to_set()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddA2AServer(o =>
        {
            o.PublicBaseUrl = "http://localhost:5200";
            o.GrpcPorts["echo"] = 5200;
        });
        builder.ConfigureA2AKestrel();

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var app = builder.Build();
            _ = app.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        });

        Assert.Contains("PublicBaseUrl (5200)", ex.Message);
        Assert.Contains("GrpcPorts (5200)", ex.Message);
        Assert.Contains("Set a different port in PublicBaseUrl or give each agent its own port in GrpcPorts", ex.Message);
    }
}
