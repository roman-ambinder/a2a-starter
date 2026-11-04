using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Ambinder.A2A.AspNetCore.Grpc;
using Ambinder.A2A.Integration.Tests.Fixtures;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Server.Security;
using Ambinder.A2A.Server.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Proto = Lf.A2a.V1;

namespace Ambinder.A2A.Integration.Tests.Security;

/// <summary>
/// Task 5: agents need an authenticated caller unless they opt in with <see cref="AgentAccess.Anonymous"/>, on JSON-RPC, REST and
/// gRPC. The extended card is the exception to the opt-in: it always needs a non-anonymous caller. The server listens on
/// ephemeral loopback ports, so no fixed port is used.
/// </summary>
public sealed class AnonymousAccessTests
{
    private const string Secure = "secure";
    private const string Open = "open";
    private const string AppClaims = "tid=t1;azp=app1";
    private const string OtherAppClaims = "tid=t1;azp=app2";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] Operations =
    [
        "SendMessage", "GetTask", "ListTasks", "CancelTask", "SubscribeToTask",
        "CreateTaskPushNotificationConfig", "GetTaskPushNotificationConfig", "ListTaskPushNotificationConfigs",
        "DeleteTaskPushNotificationConfig", "GetExtendedAgentCard",
    ];

    public static TheoryData<string, string> EveryOperation()
    {
        var data = new TheoryData<string, string>();
        foreach (var binding in new[] { "JSONRPC", "REST", "GRPC" })
            foreach (var op in Operations)
                data.Add(binding, op);
        return data;
    }

    [Theory, MemberData(nameof(EveryOperation))]
    public async Task Default_agent_refuses_an_unauthenticated_caller_on_every_operation(string binding, string operation)
    {
        await using var server = await AccessServer.StartAsync();

        var outcome = await server.CallAsync(binding, Secure, operation, claims: null);

        Assert.True(outcome.Unauthenticated, $"{binding} {operation}: {outcome}");
        Assert.True(outcome.Challenged || binding == "GRPC", $"{binding} {operation} sent no WWW-Authenticate challenge");
        Assert.True(outcome.BodyEmpty, $"{binding} {operation} 401 had a body");
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Cards_and_the_push_jwks_stay_public(string _)
    {
        await using var server = await AccessServer.StartAsync();
        using var http = new HttpClient();

        foreach (var path in new[] { $"/agents/{Secure}/.well-known/agent-card.json", $"/agents/{Open}/.well-known/agent-card.json", "/.well-known/a2a-push-jwks.json" })
        {
            using var response = await http.GetAsync($"http://127.0.0.1:{server.HttpPort}{path}", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task The_401_message_names_the_agent_and_the_fix()
    {
        await using var server = await AccessServer.StartAsync();
        var resolver = server.App.Services.GetRequiredService<Ambinder.A2A.AspNetCore.Hosting.IA2ACallerResolver>();
        var context = new DefaultHttpContext { RequestServices = server.App.Services };

        var ex = await Assert.ThrowsAsync<A2AAuthException>(async () => await resolver.ResolveAsync(context, Secure, null, Ct));

        Assert.Equal(401, ex.HttpStatus);
        Assert.Equal("Authentication is required. Send a bearer token for agent 'secure', or mark the agent AllowAnonymous().", ex.Message);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Anonymous_agent_serves_an_unauthenticated_caller_as_owner_anonymous(string binding)
    {
        await using var server = await AccessServer.StartAsync();
        var transport = Normalize(binding);

        var taskId = await server.SendAsync(transport, Open, claims: null);

        var saved = server.Store.Saved.Where(s => s.Id == taskId).ToList();
        Assert.NotEmpty(saved);
        Assert.All(saved, s => Assert.Equal("anonymous", s.Owner));
        Assert.True(await server.GetAsync(transport, Open, taskId, claims: null), "GetTask by id works for an anonymous caller");
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Anonymous_caller_lists_an_empty_page_without_touching_the_store(string binding)
    {
        await using var server = await AccessServer.StartAsync();
        var transport = Normalize(binding);
        await server.SendAsync(transport, Open, claims: null);

        var page = await server.ListAsync(transport, Open, claims: null);

        Assert.Equal(0, page.Count);
        Assert.Equal("", page.NextPageToken);
        Assert.Equal(0, server.Store.ListCalls);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Authenticated_caller_on_an_anonymous_agent_keeps_its_own_owner(string binding)
    {
        await using var server = await AccessServer.StartAsync();
        var transport = Normalize(binding);
        var anonymousTask = await server.SendAsync(transport, Open, claims: null);

        var ownTask = await server.SendAsync(transport, Open, AppClaims);

        Assert.Contains(server.Store.Saved, s => s.Id == ownTask && s.Owner == "tid:t1/app:app1");
        var page = await server.ListAsync(transport, Open, AppClaims);
        Assert.Equal(1, page.Count);
        Assert.False(await server.GetAsync(transport, Open, anonymousTask, AppClaims), "the anonymous task is not the app's");
        Assert.False(await server.GetAsync(transport, Open, ownTask, OtherAppClaims), "another app does not see the task");
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Extended_card_needs_an_authenticated_caller_even_on_an_anonymous_agent(string binding)
    {
        await using var server = await AccessServer.StartAsync();
        var transport = Normalize(binding);

        var anonymous = await server.CallAsync(transport, Open, "GetExtendedAgentCard", claims: null);
        var authenticated = await server.CallAsync(transport, Open, "GetExtendedAgentCard", AppClaims);

        Assert.True(anonymous.Unauthenticated, anonymous.ToString());
        Assert.True(anonymous.Challenged || transport == "GRPC");
        Assert.True(anonymous.BodyEmpty);
        Assert.True(authenticated.Succeeded, authenticated.ToString());
    }

    private static string Normalize(string binding) => binding switch { "JSONRPC" => "JSONRPC", "GRPC" => "GRPC", _ => "REST" };

    /// <summary>The result of one raw call.</summary>
    private sealed record Outcome(bool Unauthenticated, bool Challenged, bool BodyEmpty, bool Succeeded, string Text)
    {
        public override string ToString() => Text;
    }

    /// <summary>An A2A server on ephemeral ports with an authenticated agent, an anonymous agent and a store that counts calls.</summary>
    private sealed class AccessServer : IAsyncDisposable
    {
        public RecordingStore Store { get; } = new();
        public WebApplication App { get; private set; } = null!;
        public int HttpPort { get; private set; }
        private readonly Dictionary<string, int> _grpcPorts = [];

        public static async Task<AccessServer> StartAsync()
        {
            var server = new AccessServer();
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.Listen(IPAddress.Loopback, 0);
                k.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2);
                k.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2);
            });
            builder.Services.AddAuthentication(HeaderClaimsHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, HeaderClaimsHandler>(HeaderClaimsHandler.SchemeName, null);
            builder.Services.AddSingleton<ITaskStore>(server.Store);
            builder.Services
                .AddA2AServer(o => o.PublicBaseUrl = "http://127.0.0.1:1")
                .AddA2AGrpc()
                .AddAgent<EchoAgent>(Secure, _ => MinimalServer.Card(Secure, extended: true), _ => MinimalServer.Card(Secure, extended: true))
                .AddAgent<EchoAgent>(Open, _ => MinimalServer.Card(Open, extended: true), _ => MinimalServer.Card(Open, extended: true),
                    access: AgentAccess.Anonymous);

            server.App = builder.Build();
            server.App.UseAuthentication();
            server.App.MapA2A();
            server.App.MapA2AGrpc();
            await server.App.StartAsync();

            var addresses = server.App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.ToList();
            server.HttpPort = new Uri(addresses[0]).Port;
            server._grpcPorts[Secure] = new Uri(addresses[1]).Port;
            server._grpcPorts[Open] = new Uri(addresses[2]).Port;
            var grpcPorts = server.App.Services.GetRequiredService<IOptions<A2AServerOptions>>().Value.GrpcPorts;
            foreach (var (name, port) in server._grpcPorts) grpcPorts[name] = port;
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }

        private string Url(string agent, string path) => $"http://127.0.0.1:{HttpPort}/agents/{agent}/{path}";

        private GrpcChannel Channel(string agent) => GrpcChannel.ForAddress($"http://127.0.0.1:{_grpcPorts[agent]}");

        private static Metadata GrpcMetadata(string? claims)
        {
            var metadata = new Metadata { { A2AConstants.GrpcVersionKey, "1.0" } };
            if (claims is not null) metadata.Add(HeaderClaimsHandler.Header.ToLowerInvariant(), claims);
            return metadata;
        }

        private static HttpRequestMessage Http(HttpMethod method, string url, string? claims, string? json = null)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add(A2AConstants.VersionHeader, "1.0");
            if (claims is not null) request.Headers.Add(HeaderClaimsHandler.Header, claims);
            if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return request;
        }

        private const string SendJson = """{"message":{"messageId":"m-1","role":"ROLE_USER","parts":[{"text":"hi"}]},"configuration":{"returnImmediately":false}}""";

        /// <summary>Calls one operation with placeholder ids. Used where the answer matters (401 or success), not the content.</summary>
        public async Task<Outcome> CallAsync(string binding, string agent, string operation, string? claims)
        {
            if (binding == "GRPC") return await CallGrpcAsync(agent, operation, claims);

            using var http = new HttpClient();
            using var request = binding == "JSONRPC" ? JsonRpcRequest(agent, operation, claims) : RestRequest(agent, operation, claims);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
            var body = await response.Content.ReadAsStringAsync(Ct);
            return new Outcome(response.StatusCode == HttpStatusCode.Unauthorized, response.Headers.WwwAuthenticate.Count > 0, body.Length == 0,
                response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode} {body}");
        }

        private HttpRequestMessage JsonRpcRequest(string agent, string operation, string? claims)
        {
            var parameters = operation switch
            {
                "SendMessage" => SendJson,
                "CreateTaskPushNotificationConfig" => """{"taskId":"t1","url":"https://example.invalid/hook"}""",
                "GetTaskPushNotificationConfig" or "DeleteTaskPushNotificationConfig" => """{"taskId":"t1","id":"c1"}""",
                "ListTaskPushNotificationConfigs" => """{"taskId":"t1"}""",
                "ListTasks" or "GetExtendedAgentCard" => "{}",
                _ => """{"id":"t1"}""",
            };
            return Http(HttpMethod.Post, Url(agent, "jsonrpc"), claims, $$"""{"jsonrpc":"2.0","id":1,"method":"{{operation}}","params":{{parameters}}}""");
        }

        private HttpRequestMessage RestRequest(string agent, string operation, string? claims) => operation switch
        {
            "SendMessage" => Http(HttpMethod.Post, Url(agent, "rest/message:send"), claims, SendJson),
            "GetTask" => Http(HttpMethod.Get, Url(agent, "rest/tasks/t1"), claims),
            "ListTasks" => Http(HttpMethod.Get, Url(agent, "rest/tasks"), claims),
            "CancelTask" => Http(HttpMethod.Post, Url(agent, "rest/tasks/t1:cancel"), claims, "{}"),
            "SubscribeToTask" => Http(HttpMethod.Get, Url(agent, "rest/tasks/t1:subscribe"), claims),
            "CreateTaskPushNotificationConfig" => Http(HttpMethod.Post, Url(agent, "rest/tasks/t1/pushNotificationConfigs"), claims,
                """{"url":"https://example.invalid/hook"}"""),
            "GetTaskPushNotificationConfig" => Http(HttpMethod.Get, Url(agent, "rest/tasks/t1/pushNotificationConfigs/c1"), claims),
            "ListTaskPushNotificationConfigs" => Http(HttpMethod.Get, Url(agent, "rest/tasks/t1/pushNotificationConfigs"), claims),
            "DeleteTaskPushNotificationConfig" => Http(HttpMethod.Delete, Url(agent, "rest/tasks/t1/pushNotificationConfigs/c1"), claims),
            "GetExtendedAgentCard" => Http(HttpMethod.Get, Url(agent, "rest/extendedAgentCard"), claims),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };

        private async Task<Outcome> CallGrpcAsync(string agent, string operation, string? claims)
        {
            using var channel = Channel(agent);
            var client = new Proto.A2AService.A2AServiceClient(channel);
            var metadata = GrpcMetadata(claims);
            try
            {
                switch (operation)
                {
                    case "SendMessage":
                        await client.SendMessageAsync(new Proto.SendMessageRequest { Message = GrpcMessage() }, metadata, cancellationToken: Ct);
                        break;
                    case "GetTask": await client.GetTaskAsync(new Proto.GetTaskRequest { Id = "t1" }, metadata, cancellationToken: Ct); break;
                    case "ListTasks": await client.ListTasksAsync(new Proto.ListTasksRequest(), metadata, cancellationToken: Ct); break;
                    case "CancelTask": await client.CancelTaskAsync(new Proto.CancelTaskRequest { Id = "t1" }, metadata, cancellationToken: Ct); break;
                    case "SubscribeToTask":
                        using (var call = client.SubscribeToTask(new Proto.SubscribeToTaskRequest { Id = "t1" }, metadata, cancellationToken: Ct))
                            await call.ResponseStream.MoveNext(Ct);
                        break;
                    case "CreateTaskPushNotificationConfig":
                        await client.CreateTaskPushNotificationConfigAsync(
                            new Proto.TaskPushNotificationConfig { TaskId = "t1", Url = "https://example.invalid/hook" }, metadata, cancellationToken: Ct);
                        break;
                    case "GetTaskPushNotificationConfig":
                        await client.GetTaskPushNotificationConfigAsync(new Proto.GetTaskPushNotificationConfigRequest { TaskId = "t1", Id = "c1" }, metadata, cancellationToken: Ct);
                        break;
                    case "ListTaskPushNotificationConfigs":
                        await client.ListTaskPushNotificationConfigsAsync(new Proto.ListTaskPushNotificationConfigsRequest { TaskId = "t1" }, metadata, cancellationToken: Ct);
                        break;
                    case "DeleteTaskPushNotificationConfig":
                        await client.DeleteTaskPushNotificationConfigAsync(new Proto.DeleteTaskPushNotificationConfigRequest { TaskId = "t1", Id = "c1" }, metadata, cancellationToken: Ct);
                        break;
                    case "GetExtendedAgentCard":
                        await client.GetExtendedAgentCardAsync(new Proto.GetExtendedAgentCardRequest(), metadata, cancellationToken: Ct);
                        break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
                }
                return new Outcome(false, false, true, true, "OK");
            }
            catch (RpcException ex)
            {
                return new Outcome(ex.StatusCode == StatusCode.Unauthenticated, false, true, false, $"{ex.StatusCode} {ex.Status.Detail}");
            }
        }

        private static Proto.Message GrpcMessage() => new()
        {
            MessageId = "m-1",
            Role = Proto.Role.User,
            Parts = { new Proto.Part { Text = "hi" } },
        };

        /// <summary>Sends a message and returns the new task id.</summary>
        public async Task<string> SendAsync(string binding, string agent, string? claims)
        {
            if (binding == "GRPC")
            {
                using var channel = Channel(agent);
                var response = await new Proto.A2AService.A2AServiceClient(channel)
                    .SendMessageAsync(new Proto.SendMessageRequest { Message = GrpcMessage() }, GrpcMetadata(claims), cancellationToken: Ct);
                return response.Task.Id;
            }

            using var http = new HttpClient();
            using var request = binding == "JSONRPC" ? JsonRpcRequest(agent, "SendMessage", claims) : RestRequest(agent, "SendMessage", claims);
            using var result = await http.SendAsync(request, Ct);
            var json = JsonNode.Parse(await result.Content.ReadAsStringAsync(Ct))!;
            return (binding == "JSONRPC" ? json["result"]! : json)["task"]!["id"]!.GetValue<string>();
        }

        /// <summary>True when GetTask finds the task for this caller.</summary>
        public async Task<bool> GetAsync(string binding, string agent, string taskId, string? claims)
        {
            if (binding == "GRPC")
            {
                using var channel = Channel(agent);
                try
                {
                    await new Proto.A2AService.A2AServiceClient(channel).GetTaskAsync(new Proto.GetTaskRequest { Id = taskId }, GrpcMetadata(claims), cancellationToken: Ct);
                    return true;
                }
                catch (RpcException)
                {
                    return false;
                }
            }

            using var http = new HttpClient();
            using var request = binding == "JSONRPC"
                ? Http(HttpMethod.Post, Url(agent, "jsonrpc"), claims, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"GetTask\",\"params\":{\"id\":\"" + taskId + "\"}}")
                : Http(HttpMethod.Get, Url(agent, $"rest/tasks/{taskId}"), claims);
            using var response = await http.SendAsync(request, Ct);
            if (!response.IsSuccessStatusCode) return false;
            return binding != "JSONRPC" || JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["error"] is null;
        }

        /// <summary>Lists the caller's tasks: how many and the next page token.</summary>
        public async Task<(int Count, string NextPageToken)> ListAsync(string binding, string agent, string? claims)
        {
            if (binding == "GRPC")
            {
                using var channel = Channel(agent);
                var page = await new Proto.A2AService.A2AServiceClient(channel).ListTasksAsync(new Proto.ListTasksRequest(), GrpcMetadata(claims), cancellationToken: Ct);
                return (page.Tasks.Count, page.NextPageToken);
            }

            using var http = new HttpClient();
            using var request = binding == "JSONRPC" ? JsonRpcRequest(agent, "ListTasks", claims) : RestRequest(agent, "ListTasks", claims);
            using var response = await http.SendAsync(request, Ct);
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
            var result = binding == "JSONRPC" ? json["result"]! : json;
            return (result["tasks"]?.AsArray().Count ?? 0, result["nextPageToken"]?.GetValue<string>() ?? "");
        }
    }

    /// <summary>The in-memory store, plus a count of <c>ListAsync</c> calls and a record of saved owners.</summary>
    private sealed class RecordingStore : ITaskStore
    {
        private readonly ITaskStore _inner = (ITaskStore)Activator.CreateInstance(typeof(InMemoryTaskStore), nonPublic: true)!;
        private int _listCalls;

        public ConcurrentQueue<(string Id, string Owner)> Saved { get; } = new();
        public int ListCalls => Volatile.Read(ref _listCalls);

        public ValueTask<StoredTask?> GetAsync(string agentName, string taskId, CancellationToken ct) => _inner.GetAsync(agentName, taskId, ct);

        public ValueTask SaveAsync(StoredTask task, CancellationToken ct)
        {
            Saved.Enqueue((task.Task.Id, task.Owner));
            return _inner.SaveAsync(task, ct);
        }

        public ValueTask<ListTasksResponse> ListAsync(string agentName, string owner, ListTasksRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _listCalls);
            return _inner.ListAsync(agentName, owner, request, ct);
        }
    }

    /// <summary>
    /// Test scheme: <c>X-Test-Claims: type=value;type=value</c> makes an authenticated principal with those claims; no header is no
    /// result. A challenge sets <c>WWW-Authenticate</c> and 401.
    /// </summary>
    private sealed class HeaderClaimsHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "HeaderClaims";
        public const string Header = "X-Test-Claims";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers[Header].ToString();
            if (header.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = header.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .Select(parts => new Claim(parts[0], parts[1]));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        }
    }
}
