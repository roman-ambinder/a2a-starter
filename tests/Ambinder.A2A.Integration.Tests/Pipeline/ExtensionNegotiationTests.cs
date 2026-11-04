using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Tests.Shared;
using Ambinder.A2A.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.Integration.Tests.Pipeline;

/// <summary>Pipeline step 4: the extension negotiation middleware echoes the active extensions, and forwarded calls skip it (B5).</summary>
public sealed class ExtensionNegotiationTests
{
    private const string Agent = "a";
    private const string Declared = "urn:test:declared";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Recorder
    {
        private readonly List<string> _names = [];
        public void Add(string name) { lock (_names) _names.Add(name); }
        public string[] Names { get { lock (_names) return [.. _names]; } }
    }

    private sealed class RecordApp(Recorder recorder) : IA2AMiddleware
    {
        public ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
        {
            recorder.Add("app");
            return next(context);
        }
    }

    private sealed class RecordAuth(Recorder recorder) : IA2AMiddleware
    {
        public ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
        {
            recorder.Add("auth");
            return next(context);
        }
    }

    private sealed class NoopAgent : IAgentHandler
    {
        public ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private static AgentCard Card() => new()
    {
        Name = Agent,
        Version = "1.0.0",
        Capabilities = new AgentCapabilities
        {
            Streaming = true, PushNotifications = true, ExtendedAgentCard = true,
            Extensions = [new AgentExtension { Uri = Declared }],
        },
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
    };

    private static Task<A2ATestServer> StartAsync()
        => A2ATestServer.StartAsync(a => a.UseMiddleware<RecordApp>().UseAuthorizationMiddleware<RecordAuth>().AddAgent<NoopAgent>(Agent, _ => Card(), _ => Card()),
            s =>
            {
                s.AddSingleton<Recorder>();
                s.Configure<A2AServerOptions>(o => o.Push.AllowPrivateNetworks = true);
            },
            o => o.ConfigureApp = app => app.Use((http, next) =>
            {
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "alice")], "test"));
                return next(http);
            }));

    private static string Message() => new JsonObject
    {
        ["messageId"] = "m1", ["role"] = "ROLE_USER", ["parts"] = new JsonArray(new JsonObject { ["text"] = "hi" }),
    }.ToJsonString();

    private static string SendBody() => new JsonObject { ["message"] = JsonNode.Parse(Message()) }.ToJsonString();

    // Every operation, with a valid request. Most answer with an error (the task does not exist); the header is written either way.
    public static TheoryData<string, JsonNode?> RpcOperations => new()
    {
        { "SendMessage", new JsonObject { ["message"] = JsonNode.Parse(Message()) } },
        { "SendStreamingMessage", new JsonObject { ["message"] = JsonNode.Parse(Message()) } },
        { "GetTask", new JsonObject { ["id"] = "x" } },
        { "ListTasks", new JsonObject() },
        { "CancelTask", new JsonObject { ["id"] = "x" } },
        { "SubscribeToTask", new JsonObject { ["id"] = "x" } },
        { "CreateTaskPushNotificationConfig", new JsonObject { ["taskId"] = "x", ["url"] = "http://hooks.test/hook" } },
        { "GetTaskPushNotificationConfig", new JsonObject { ["taskId"] = "x", ["id"] = "c" } },
        { "ListTaskPushNotificationConfigs", new JsonObject { ["taskId"] = "x" } },
        { "DeleteTaskPushNotificationConfig", new JsonObject { ["taskId"] = "x", ["id"] = "c" } },
        { "GetExtendedAgentCard", new JsonObject() },
    };

    public static TheoryData<string, string, string?> RestOperations => new()
    {
        { "POST", "message:send", SendBody() },
        { "POST", "message:stream", SendBody() },
        { "GET", "tasks/x", null },
        { "GET", "tasks", null },
        { "POST", "tasks/x:cancel", "{}" },
        { "GET", "tasks/x:subscribe", null },
        { "POST", "tasks/x/pushNotificationConfigs", """{"url":"http://hooks.test/hook"}""" },
        { "GET", "tasks/x/pushNotificationConfigs/c", null },
        { "GET", "tasks/x/pushNotificationConfigs", null },
        { "DELETE", "tasks/x/pushNotificationConfigs/c", null },
        { "GET", "extendedAgentCard", null },
    };

    private static async Task<HttpResponseMessage> SendRpcAsync(A2ATestServer server, string method, JsonNode? parameters, string extensions)
    {
        var body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters?.DeepClone() }.ToJsonString();
        var request = new HttpRequestMessage(HttpMethod.Post, $"agents/{Agent}/jsonrpc") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("A2A-Version", "1.0");
        request.Headers.Add("A2A-Extensions", extensions);
        return await server.CreateHttpClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
    }

    private static async Task<HttpResponseMessage> SendRestAsync(A2ATestServer server, string verb, string path, string? json, string extensions)
    {
        var request = new HttpRequestMessage(new HttpMethod(verb), $"agents/{Agent}/rest/{path}");
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/a2a+json");
        request.Headers.Add("A2A-Version", "1.0");
        request.Headers.Add("A2A-Extensions", extensions);
        return await server.CreateHttpClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
    }

    [Theory]
    [MemberData(nameof(RpcOperations))]
    public async Task DeclaredExtensionIsEchoedOnJsonRpc(string method, JsonNode? parameters)
    {
        await using var server = await StartAsync();

        using var response = await SendRpcAsync(server, method, parameters, $"{Declared}, urn:test:unknown");

        Assert.Equal([Declared], response.Headers.GetValues("A2A-Extensions"));
    }

    [Theory]
    [MemberData(nameof(RestOperations))]
    public async Task DeclaredExtensionIsEchoedOnRest(string verb, string path, string? json)
    {
        await using var server = await StartAsync();

        using var response = await SendRestAsync(server, verb, path, json, $"{Declared}, urn:test:unknown");

        Assert.Equal([Declared], response.Headers.GetValues("A2A-Extensions"));
    }

    [Fact]
    public async Task UndeclaredExtensionIsNotEchoed()
    {
        await using var server = await StartAsync();

        using var rpc = await SendRpcAsync(server, "ListTasks", new JsonObject(), "urn:test:unknown");
        using var rest = await SendRestAsync(server, "GET", "tasks", null, "urn:test:unknown");

        Assert.Equal(HttpStatusCode.OK, rpc.StatusCode);
        Assert.False(rpc.Headers.Contains("A2A-Extensions"));
        Assert.Equal(HttpStatusCode.OK, rest.StatusCode);
        Assert.False(rest.Headers.Contains("A2A-Extensions"));
    }

    [Fact]
    public async Task MissingRequiredExtensionFailsInProcessCallsToo()
    {
        await using var server = await A2ATestServer.StartAsync(a => a.AddAgent<NoopAgent>(Agent, _ => RequiredCard(), null));
        var call = new A2ACallContext(Agent, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "alice")], "test")), "alice", null,
            A2AConstants.ProtocolVersion, []);

        var ex = await Assert.ThrowsAsync<A2AException>(() => server.Handler.ListTasksAsync(call, new ListTasksRequest(), Ct));

        Assert.Equal(A2AErrorCode.ExtensionSupportRequired, ex.Code);
        await server.Handler.ListTasksAsync(call with { RequestedExtensions = [Declared] }, new ListTasksRequest(), Ct);
    }

    private static AgentCard RequiredCard() => Card() with
    {
        Capabilities = new AgentCapabilities { Extensions = [new AgentExtension { Uri = Declared, Required = true }] },
    };

    [Fact]
    public async Task ForwardedCallRunsOnlyTracingAndHandler()
    {
        await using var server = await StartAsync();
        var recorder = server.Services.GetRequiredService<Recorder>();
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        var metadata = new A2AResponseMetadata();
        var call = new A2ACallContext(Agent, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "alice")], "test")), "alice", null,
            A2AConstants.ProtocolVersion, [Declared]) { IsForwarded = true, ResponseMetadata = metadata };

        await Assert.ThrowsAsync<A2AException>(() => server.Handler.GetTaskAsync(call, new GetTaskRequest { Id = "missing" }, Ct));

        Assert.Empty(recorder.Names); // neither the app middleware nor the slot 300 middleware ran
        Assert.Single(collector.Stopped, a => a.OperationName == "a2a.server GetTask");
        Assert.Empty(metadata.Entries); // no A2A-Extensions entry
    }
}
