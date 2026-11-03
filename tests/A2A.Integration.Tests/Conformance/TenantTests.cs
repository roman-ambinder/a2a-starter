using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using A2A.Client;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;
using A2A.Server;
using A2A.Server.Hosting;
using A2A.Server.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto = Lf.A2a.V1;

namespace A2A.Integration.Tests.Conformance;

/// <summary>
/// Spec 8.3.2 / design D8: an agent with <see cref="A2AServerOptions.InterfaceTenants"/> writes the tenant on every card interface,
/// rejects a request tenant that differs (<c>InvalidParams</c>) and treats an empty one as its own. Runs on a small server of its own,
/// so the tenant does not touch the other tests' agents.
/// </summary>
public sealed class TenantTests(TenantTests.TenantServer server) : IClassFixture<TenantTests.TenantServer>
{
    private const string Mismatch = "tenant 't2' does not match this interface (tenant 't1'). Leave tenant empty or send 't1'.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Card_lists_the_tenant_on_every_interface()
    {
        using var http = new HttpClient();
        var card = await new A2ACardResolver(http).GetAgentCardAsync(new Uri($"{TenantServer.BaseUrl}/agents/echo"), Ct);

        Assert.Equal(3, card.SupportedInterfaces.Count);
        Assert.All(card.SupportedInterfaces, i => Assert.Equal("t1", i.Tenant));
    }

    [Fact]
    public async Task Jsonrpc_rejects_a_different_tenant()
    {
        using var http = new HttpClient();
        var body = new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "SendMessage",
            ["params"] = new JsonObject { ["tenant"] = "t2", ["message"] = MessageJson() },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{TenantServer.BaseUrl}/agents/echo/jsonrpc")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("A2A-Version", "1.0");

        using var response = await http.SendAsync(request, Ct);

        var error = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["error"]!;
        Assert.Equal((int)A2AErrorCode.InvalidParams, error["code"]!.GetValue<int>());
        Assert.Equal(Mismatch, error["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Rest_rejects_a_different_tenant_in_the_path()
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{TenantServer.BaseUrl}/agents/echo/rest/t2/message:send")
        {
            Content = new StringContent(new JsonObject { ["message"] = MessageJson() }.ToJsonString(), Encoding.UTF8, "application/a2a+json"),
        };
        request.Headers.Add("A2A-Version", "1.0");

        using var response = await http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["error"]!;
        Assert.Equal("INVALID_ARGUMENT", error["status"]!.GetValue<string>());
        Assert.Equal(Mismatch, error["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Grpc_rejects_a_different_tenant()
    {
        using var channel = GrpcChannel.ForAddress($"http://127.0.0.1:{TenantServer.GrpcPort}");
        var client = new Proto.A2AService.A2AServiceClient(channel);
        var request = new Proto.SendMessageRequest
        {
            Tenant = "t2",
            Message = new Proto.Message { MessageId = Guid.NewGuid().ToString("N"), Role = Proto.Role.User, Parts = { new Proto.Part { Text = "hi" } } },
        };

        var error = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.SendMessageAsync(request, new Metadata { { "a2a-version", "1.0" } }, cancellationToken: Ct));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Equal(Mismatch, error.Status.Detail);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Empty_request_tenant_is_filled_with_the_interface_tenant(string binding)
    {
        // A card without interface tenants makes the client send none, as a client that ignores the card would.
        await using var client = await CreateClientAsync(binding, keepCardTenant: false);
        server.HandlerCalls.Clear();

        await client.SendMessageAsync(Send(), Ct);

        Assert.Equal("t1", Assert.Single(server.HandlerCalls, c => c.Operation == "SendMessage").Tenant);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task The_interface_tenant_itself_is_accepted(string binding)
    {
        await using var client = await CreateClientAsync(binding, keepCardTenant: true);
        server.HandlerCalls.Clear();

        await client.SendMessageAsync(Send(), Ct);

        Assert.Equal("t1", Assert.Single(server.HandlerCalls, c => c.Operation == "SendMessage").Tenant);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Client_refuses_a_request_tenant_that_differs_from_the_interface(string binding)
    {
        await using var client = await CreateClientAsync(binding, keepCardTenant: true);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendMessageAsync(Send() with { Tenant = "t2" }, Ct));
    }

    private static async Task<IA2AClient> CreateClientAsync(string binding, bool keepCardTenant)
    {
        var http = new HttpClient();
        var card = await new A2ACardResolver(http).GetAgentCardAsync(new Uri($"{TenantServer.BaseUrl}/agents/echo"), Ct);
        if (!keepCardTenant)
            card = card with { SupportedInterfaces = [.. card.SupportedInterfaces.Select(i => i with { Tenant = null })] };
        return A2AClientFactory.Create(card, new A2AClientOptions { HttpClient = http, PreferredBinding = binding });
    }

    private static SendMessageRequest Send() => new() { Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText("hi")] } };

    private static JsonObject MessageJson() => new()
    {
        ["messageId"] = Guid.NewGuid().ToString("N"),
        ["role"] = "ROLE_USER",
        ["parts"] = new JsonArray(new JsonObject { ["text"] = "hi" }),
    };

    /// <summary>An echo agent whose interfaces carry tenant <c>t1</c>.</summary>
    public sealed class TenantServer : IAsyncLifetime
    {
        private static readonly int PortBase = MinimalServer.PortBase + 400;
        public static int HttpPort => PortBase;
        public static int GrpcPort => PortBase + 1;
        public static string BaseUrl => $"http://127.0.0.1:{HttpPort}";

        private WebApplication? _app;

        public ConcurrentQueue<(string Operation, string? Tenant)> HandlerCalls { get; } = new();

        public async ValueTask InitializeAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.ConfigureA2AKestrel();
            builder.Services.AddSingleton<IA2ARequestHandler>(sp => new RecordingHandler(sp.GetRequiredService<TaskManager>(), HandlerCalls));
            builder.Services
                .AddA2AServer(o =>
                {
                    o.PublicBaseUrl = BaseUrl;
                    o.DefaultAgent = "echo";
                    o.GrpcPorts["echo"] = GrpcPort;
                    o.InterfaceTenants["echo"] = "t1";
                })
                .AddAgent<EchoAgent>("echo", _ => MinimalServer.Card("echo", extended: false));
            _app = builder.Build();
            _app.MapA2A();
            _app.MapA2AGrpc();
            await _app.StartAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (_app is null) return;
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
