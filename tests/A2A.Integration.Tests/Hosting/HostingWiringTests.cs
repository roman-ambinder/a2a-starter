using A2A.Integration.Tests.Fixtures;
using A2A.Server;
using A2A.Server.Agents;
using A2A.Server.Hosting;
using A2A.Server.Push;
using A2A.Server.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace A2A.Integration.Tests.Hosting;

[Collection(MinimalServerCollection.Name)]
public sealed class HostingWiringTests(MinimalServer server)
{
    private IServiceProvider Services => server.App.Services;

    [Fact]
    public void Push_options_are_the_server_options_instance_with_issuer_set()
    {
        var a2a = Services.GetRequiredService<IOptions<A2AServerOptions>>().Value;
        var push = Services.GetRequiredService<IOptions<PushOptions>>().Value;
        Assert.Same(a2a.Push, push);
        Assert.Equal(MinimalServer.BaseUrl, push.Issuer);
        Assert.True(push.AllowPrivateNetworks);
    }

    [Fact]
    public void Push_sender_is_observer_and_hosted_service_and_handler_is_task_manager()
    {
        var sender = Services.GetRequiredService<PushNotificationSender>();
        Assert.Contains(sender, Services.GetServices<ITaskEventObserver>());
        Assert.Contains(sender, Services.GetServices<IHostedService>().OfType<PushNotificationSender>());
        Assert.Same(Services.GetRequiredService<TaskManager>(), Services.GetRequiredService<IA2ARequestHandler>());
    }

    [Fact]
    public void Catalog_is_built_from_the_factories_with_generated_interfaces()
    {
        var catalog = Services.GetRequiredService<AgentCatalog>();
        var echo = catalog.Get(MinimalServer.EchoAgentName);
        Assert.Equal(3, echo.PublicCard.SupportedInterfaces.Count);
        Assert.Equal(3, echo.ExtendedCard!.SupportedInterfaces.Count);
        Assert.Equal(MinimalServer.InvokerPolicy, catalog.Get(MinimalServer.ProtectedAgentName).AuthorizationPolicy);
    }

    [Fact]
    public void AddAgent_rejects_duplicate_and_unsafe_names()
    {
        var builder = new ServiceCollection().AddA2AServer(o => o.PublicBaseUrl = "http://localhost");
        builder.AddAgent<EchoAgent>("a", _ => new());
        Assert.Throws<ArgumentException>(() => builder.AddAgent<EchoAgent>("a", _ => new()));
        Assert.Throws<ArgumentException>(() => builder.AddAgent<EchoAgent>("a/b", _ => new()));
    }

    [Fact]
    public void Grpc_interface_is_omitted_without_a_port_and_ipv6_hosts_are_bracketed()
    {
        var options = new A2AServerOptions { PublicBaseUrl = "http://[::1]:5200/" };
        Assert.Equal(2, AgentCardInterfaces.For("x", options).Count);
        options.GrpcPorts["x"] = 5201;
        var list = AgentCardInterfaces.For("x", options);
        Assert.Equal("http://[::1]:5200/agents/x/jsonrpc", list[0].Url);
        Assert.Equal("http://[::1]:5201", list[2].Url);
    }
}
