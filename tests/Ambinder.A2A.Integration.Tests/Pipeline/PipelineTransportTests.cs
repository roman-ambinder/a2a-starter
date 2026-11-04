using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Ambinder.A2A.Client;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.Integration.Tests.Pipeline;

/// <summary>Every transport runs the middleware pipeline, and response metadata reaches the client.</summary>
public sealed class PipelineTransportTests
{
    private const string Agent = "a";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<A2ABinding> Bindings => [.. A2ABindings.All];

    // ---- test doubles ----

    private sealed class Recorder
    {
        private readonly List<A2AOperation> _operations = [];
        private readonly Lock _gate = new();
        public void Add(A2AOperation operation) { lock (_gate) _operations.Add(operation); }
        public A2AOperation[] Operations { get { lock (_gate) return [.. _operations]; } }
        public void Clear() { lock (_gate) _operations.Clear(); }
    }

    private sealed class CompletingAgent : IAgentHandler
    {
        public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
        }
    }

    private sealed class RecordOperations(Recorder recorder) : IA2AMiddleware
    {
        public ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
        {
            recorder.Add(context.Operation);
            return next(context);
        }
    }

    private sealed class SetHeaders : IA2AMiddleware
    {
        public ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
        {
            context.ResponseMetadata.Set("A2A-Test", "yes");
            context.ResponseMetadata.Set("X-Not-Allowed", "secret");
            return next(context);
        }
    }

    private sealed class RejectTaskNotFound : IA2AMiddleware
    {
        public ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
            => context.Operation == A2AOperation.GetTask && context.Request is GetTaskRequest { Id: "rejected" }
                ? throw new A2AException(A2AErrorCode.TaskNotFound, "rejected by middleware")
                : next(context);
    }

    private sealed class ThrowAfterFirstEvent : IA2AMiddleware
    {
        public async ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
        {
            var result = await next(context);
            return result.Events is { } events ? A2AOperationResult.FromEvents(Throwing(events)) : result;
        }

        private static async IAsyncEnumerable<StreamResponse> Throwing(IAsyncEnumerable<StreamResponse> events)
        {
            await foreach (var e in events)
            {
                yield return e;
                throw new A2AException(A2AErrorCode.InvalidParams, "late failure");
            }
        }
    }

    // ---- helpers ----

    private static AgentCard Card() => new()
    {
        Name = Agent,
        Version = "1.0.0",
        Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = true, ExtendedAgentCard = true },
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
    };

    private static Task<A2ATestServer> StartAsync<T>(Action<IServiceCollection>? services = null) where T : class, IA2AMiddleware
        => A2ATestServer.StartAsync(a => a.UseMiddleware<T>().AddAgent<CompletingAgent>(Agent, _ => Card(), _ => Card()),
            s =>
            {
                s.AddSingleton<Recorder>();
                s.Configure<A2AServerOptions>(o => o.Push.AllowPrivateNetworks = true);
                services?.Invoke(s);
            },
            // The extended card needs an authenticated caller.
            o => o.ConfigureApp = app => app.Use((http, next) =>
            {
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "alice")], "test"));
                return next(http);
            }));

    private static A2ACallContext Call()
        => new(Agent, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "alice")], "test")), "alice", null,
            A2AConstants.ProtocolVersion, []);

    private static SendMessageRequest Send(string text) => new()
    {
        Message = new Message { MessageId = Guid.NewGuid().ToString(), Role = Role.User, Parts = [Part.FromText(text)] },
    };

    private static async Task Drain(IAsyncEnumerable<StreamResponse> stream)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        await foreach (var _ in stream.WithCancellation(cts.Token)) { }
    }

    /// <summary>The refusals are fine: the middleware runs before the operation, which is what is recorded.</summary>
    private static async Task Try(Func<Task> action)
    {
        try { await action(); }
        catch (A2AException) { }
    }

    private static async Task RunAllOperationsAsync(IA2ARequestHandler h)
    {
        var call = Call();
        var sent = await h.SendMessageAsync(call, Send("one"), Ct);
        var id = sent.Task!.Id;
        await Drain(h.SendStreamingMessageAsync(call, Send("two"), Ct));
        await h.GetTaskAsync(call, new GetTaskRequest { Id = id }, Ct);
        await h.ListTasksAsync(call, new ListTasksRequest(), Ct);
        await Try(() => h.CancelTaskAsync(call, new CancelTaskRequest { Id = id }, Ct));
        await Try(() => Drain(h.SubscribeToTaskAsync(call, new SubscribeToTaskRequest { Id = id }, Ct)));
        var config = await h.CreateTaskPushNotificationConfigAsync(call, new TaskPushNotificationConfig { TaskId = id, Url = "http://hooks.test/hook" }, Ct);
        await h.GetTaskPushNotificationConfigAsync(call, new GetTaskPushNotificationConfigRequest { TaskId = id, Id = config.Id! }, Ct);
        await h.ListTaskPushNotificationConfigsAsync(call, new ListTaskPushNotificationConfigsRequest { TaskId = id }, Ct);
        await h.DeleteTaskPushNotificationConfigAsync(call, new DeleteTaskPushNotificationConfigRequest { TaskId = id, Id = config.Id! }, Ct);
        await h.GetExtendedAgentCardAsync(call, new GetExtendedAgentCardRequest(), Ct);
    }

    private static async Task RunAllOperationsAsync(IA2AClient c)
    {
        var sent = await c.SendMessageAsync(Send("one"), Ct);
        var id = sent.Task!.Id;
        await Drain(c.SendStreamingMessageAsync(Send("two"), Ct));
        await c.GetTaskAsync(new GetTaskRequest { Id = id }, Ct);
        await c.ListTasksAsync(new ListTasksRequest(), Ct);
        await Try(() => c.CancelTaskAsync(new CancelTaskRequest { Id = id }, Ct));
        await Try(() => Drain(c.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = id }, Ct)));
        var config = await c.CreateTaskPushNotificationConfigAsync(new TaskPushNotificationConfig { TaskId = id, Url = "http://hooks.test/hook" }, Ct);
        await c.GetTaskPushNotificationConfigAsync(new GetTaskPushNotificationConfigRequest { TaskId = id, Id = config.Id! }, Ct);
        await c.ListTaskPushNotificationConfigsAsync(new ListTaskPushNotificationConfigsRequest { TaskId = id }, Ct);
        await c.DeleteTaskPushNotificationConfigAsync(new DeleteTaskPushNotificationConfigRequest { TaskId = id, Id = config.Id! }, Ct);
        await c.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest(), Ct);
    }

    private static string SendBodyJson(string text) => new JsonObject
    {
        ["message"] = new JsonObject
        {
            ["messageId"] = Guid.NewGuid().ToString(),
            ["role"] = "ROLE_USER",
            ["parts"] = new JsonArray(new JsonObject { ["text"] = text }),
        },
    }.ToJsonString();

    /// <summary>A raw SendMessage (unary) or SendStreamingMessage (streaming) request for the binding. Headers are read; the body is not.</summary>
    private static Task<HttpResponseMessage> RawSendAsync(A2ATestServer server, A2ABinding binding, bool streaming)
    {
        var http = server.CreateHttpClient();
        HttpRequestMessage request;
        if (binding == A2ABinding.JsonRpc)
        {
            var body = new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = 1,
                ["method"] = streaming ? "SendStreamingMessage" : "SendMessage",
                ["params"] = JsonNode.Parse(SendBodyJson("hi")),
            }.ToJsonString();
            request = new HttpRequestMessage(HttpMethod.Post, $"agents/{Agent}/jsonrpc") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
        else
        {
            request = new HttpRequestMessage(HttpMethod.Post, $"agents/{Agent}/rest/{(streaming ? "message:stream" : "message:send")}")
            {
                Content = new StringContent(SendBodyJson("hi"), Encoding.UTF8, "application/a2a+json"),
            };
        }
        request.Headers.Add("A2A-Version", "1.0");
        return http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
    }

    private static async Task<string> ReadAllAsync(HttpResponseMessage response)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        return await response.Content.ReadAsStringAsync(cts.Token);
    }

    private static List<JsonNode> Frames(string sse)
        => sse.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => f.StartsWith("data:", StringComparison.Ordinal))
            .Select(f => JsonNode.Parse(f["data:".Length..])!)
            .ToList();

    // ---- tests ----

    [Theory]
    [MemberData(nameof(Bindings))]
    public async Task TestMiddlewareSeesAllOperationsOnEachTransport(A2ABinding binding)
    {
        await using var server = await StartAsync<RecordOperations>();
        var recorder = server.Services.GetRequiredService<Recorder>();

        await RunAllOperationsAsync(server.Handler);
        var inProcess = recorder.Operations;
        recorder.Clear();
        await RunAllOperationsAsync(server.CreateClient(binding));

        Assert.Equal(Enum.GetValues<A2AOperation>().Order(), inProcess.Distinct().Order());
        Assert.Equal(inProcess, recorder.Operations);
    }

    [Theory]
    [MemberData(nameof(Bindings))]
    public async Task MiddlewareHeaderReachesClient(A2ABinding binding)
    {
        await using var server = await StartAsync<SetHeaders>();

        foreach (var streaming in new[] { false, true })
        {
            using var response = await RawSendAsync(server, binding, streaming);
            await ReadAllAsync(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("yes", Assert.Single(response.Headers.GetValues("A2A-Test")));
            Assert.False(response.Headers.Contains("X-Not-Allowed"));
        }
    }

    [Theory]
    [MemberData(nameof(Bindings))]
    public async Task MiddlewareHeaderOutsideTheAllowListIsSentOnceAllowed(A2ABinding binding)
    {
        await using var server = await StartAsync<SetHeaders>(s => s.Configure<A2AServerOptions>(o => o.ResponseHeaderAllowList.Add("x-not-allowed")));

        foreach (var streaming in new[] { false, true })
        {
            using var response = await RawSendAsync(server, binding, streaming);
            await ReadAllAsync(response);
            Assert.Equal("secret", Assert.Single(response.Headers.GetValues("X-Not-Allowed")));
        }
    }

    [Fact]
    public async Task MiddlewareRejectionMapsLikeHandlerErrorOnJsonRpc()
    {
        await using var server = await StartAsync<RejectTaskNotFound>();

        var rejected = await GetTaskRpcAsync(server, "rejected");
        var missing = await GetTaskRpcAsync(server, "missing");

        Assert.Equal(-32001, rejected["error"]!["code"]!.GetValue<int>());
        Assert.Equal(missing["error"]!["code"]!.GetValue<int>(), rejected["error"]!["code"]!.GetValue<int>());
        Assert.Equal(Keys(missing["error"]!), Keys(rejected["error"]!));
        Assert.Equal("rejected by middleware", rejected["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task MiddlewareRejectionMapsLikeHandlerErrorOnRest()
    {
        await using var server = await StartAsync<RejectTaskNotFound>();
        var http = server.CreateHttpClient();

        async Task<(HttpStatusCode Status, JsonNode Body)> GetAsync(string id)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"agents/{Agent}/rest/tasks/{id}");
            request.Headers.Add("A2A-Version", "1.0");
            using var response = await http.SendAsync(request, Ct);
            return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!);
        }

        var rejected = await GetAsync("rejected");
        var missing = await GetAsync("missing");

        Assert.Equal(HttpStatusCode.NotFound, rejected.Status);
        Assert.Equal(missing.Status, rejected.Status);
        Assert.Equal(Keys(missing.Body["error"]!), Keys(rejected.Body["error"]!));
        Assert.Equal(missing.Body["error"]!["status"]!.GetValue<string>(), rejected.Body["error"]!["status"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(Bindings))]
    public async Task StreamErrorAfterFirstEventIsErrorFrame(A2ABinding binding)
    {
        await using var server = await StartAsync<ThrowAfterFirstEvent>();

        using var response = await RawSendAsync(server, binding, streaming: true);
        var frames = Frames(await ReadAllAsync(response));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, frames.Count);
        Assert.Null(frames[0]["error"]);
        Assert.NotNull(frames[1]["error"]);
    }

    [Fact]
    public void NoTransportCallsTaskManagerDirectly()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Ambinder.A2A.slnx"))) root = root.Parent;
        Assert.NotNull(root);

        var offenders = new List<string>();
        foreach (var project in new[] { "Ambinder.A2A.AspNetCore", "Ambinder.A2A.AspNetCore.Grpc" })
        {
            var dir = Path.Combine(root.FullName, "src", project);
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(dir, file);
                if (relative.StartsWith("obj", StringComparison.Ordinal) || relative.StartsWith("bin", StringComparison.Ordinal)) continue;
                if (Path.GetFileName(file).Contains("ServiceCollectionExtensions", StringComparison.Ordinal)) continue;
                if (File.ReadAllText(file).Contains("TaskManager", StringComparison.Ordinal)) offenders.Add(Path.Combine(project, relative));
            }
        }

        Assert.Empty(offenders);
    }

    private static async Task<JsonNode> GetTaskRpcAsync(A2ATestServer server, string id)
    {
        var body = new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "GetTask", ["params"] = new JsonObject { ["id"] = id },
        }.ToJsonString();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"agents/{Agent}/jsonrpc") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("A2A-Version", "1.0");
        using var response = await server.CreateHttpClient().SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
    }

    private static string[] Keys(JsonNode node) => [.. node.AsObject().Select(p => p.Key).Order()];
}
