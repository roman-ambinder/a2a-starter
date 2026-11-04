using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ambinder.A2A.Client;
using Ambinder.A2A.Client.Grpc;
using Ambinder.A2A.Client.Transports;
using Ambinder.A2A.Integration.Tests.Fixtures;
using Ambinder.A2A.Protocol;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto = Lf.A2a.V1;

namespace Ambinder.A2A.Integration.Tests.Conformance;

/// <summary>
/// Design D3 on the wire. The server rejects a message role it does not know with InvalidParams (and says why); a JSON integer role is
/// accepted; responses name enums as strings. The client reads a task state it does not know as UNSPECIFIED instead of failing.
/// </summary>
[Collection(MinimalServerCollection.Name)]
public sealed class EnumWireTests(MinimalServer server)
{
    private const string Echo = MinimalServer.EchoAgentName;
    private const string RoleMessage = "message.role is missing or has an unknown value; use ROLE_USER.";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- server: requests ----------

    public static TheoryData<string, string> UnknownRoles() => new()
    {
        { "JSONRPC", "\"ROLE_FUTURE\"" },
        { "JSONRPC", "\"user\"" }, // the 0.3 spelling is not guessed
        { "HTTP+JSON", "\"ROLE_FUTURE\"" },
        { "HTTP+JSON", "\"user\"" },
        { "GRPC", "99" },
    };

    [Theory, MemberData(nameof(UnknownRoles))]
    public async Task Unknown_role_is_invalid_params_with_the_D3_message(string binding, string role)
    {
        var (code, message) = binding == "GRPC" ? await GrpcSendRoleAsync(int.Parse(role)) : await JsonSendAsync(binding, role);

        Assert.Equal(A2AErrorCode.InvalidParams, code);
        Assert.Equal(RoleMessage, message);
    }

    [Theory]
    [InlineData("JSONRPC")]
    [InlineData("HTTP+JSON")]
    public async Task Integer_role_is_accepted_and_the_response_names_enums_as_strings(string binding)
    {
        using var http = server.CreateClient("enum-user");
        var json = await PostSendAsync(http, binding, "1");

        Assert.Null(json["error"]);
        var result = binding == "JSONRPC" ? json["result"]! : json;
        var state = result["task"]!["status"]!["state"]!;
        Assert.Equal(JsonValueKind.String, state.GetValueKind());
        Assert.Equal("TASK_STATE_COMPLETED", state.GetValue<string>());
        foreach (var message in result["task"]!["history"]?.AsArray() ?? [])
            Assert.Equal(JsonValueKind.String, message!["role"]!.GetValueKind());
    }

    private async Task<(A2AErrorCode Code, string Message)> JsonSendAsync(string binding, string role)
    {
        using var http = server.CreateClient("enum-user");
        var json = await PostSendAsync(http, binding, role);
        var error = json["error"]!;
        if (binding == "JSONRPC")
            return ((A2AErrorCode)error["code"]!.GetValue<int>(), error["message"]!.GetValue<string>());
        Assert.Equal("INVALID_ARGUMENT", error["status"]!.GetValue<string>());
        return (A2AErrorCode.InvalidParams, error["message"]!.GetValue<string>());
    }

    private static async Task<JsonNode> PostSendAsync(HttpClient http, string binding, string role)
    {
        var message = $$"""{"messageId":"{{Guid.NewGuid():N}}","role":{{role}},"parts":[{"text":"hi"}]}""";
        var (path, body) = binding == "JSONRPC"
            ? ($"agents/{Echo}/jsonrpc", $$$"""{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{{{message}}}}}""")
            : ($"agents/{Echo}/rest/message:send", $$"""{"message":{{message}}}""");
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, binding == "JSONRPC" ? "application/json" : "application/a2a+json"),
        };
        request.Headers.Add("A2A-Version", "1.0");
        using var response = await http.SendAsync(request, Ct);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
    }

    private static async Task<(A2AErrorCode, string)> GrpcSendRoleAsync(int role)
    {
        using var channel = GrpcChannel.ForAddress($"http://127.0.0.1:{MinimalServer.EchoGrpcPort}");
        var client = new Proto.A2AService.A2AServiceClient(channel);
        var request = new Proto.SendMessageRequest
        {
            Message = new Proto.Message { MessageId = Guid.NewGuid().ToString("N"), Role = (Proto.Role)role, Parts = { new Proto.Part { Text = "hi" } } },
        };
        var headers = new Metadata { { "a2a-version", "1.0" }, { MinimalServer.UserHeader.ToLowerInvariant(), "enum-user" } };
        var ex = await Assert.ThrowsAsync<RpcException>(async () => await client.SendMessageAsync(request, headers, cancellationToken: Ct));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        return (A2AErrorCode.InvalidParams, ex.Status.Detail);
    }

    // ---------- client: responses ----------

    private sealed class StubHandler(bool jsonRpc, string stateJson) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var task = $$$"""{"id":"t1","contextId":"c","status":{"state":{{{stateJson}}}}}""";
            var body = task;
            if (jsonRpc)
            {
                var id = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!["id"]!.ToJsonString();
                body = $$$"""{"jsonrpc":"2.0","id":{{{id}}},"result":{{{task}}}}""";
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, jsonRpc ? "application/json" : "application/a2a+json"),
            };
        }
    }

    public static TheoryData<string, string, TaskState> StubStates()
    {
        var data = new TheoryData<string, string, TaskState>();
        foreach (var binding in new[] { "JSONRPC", "HTTP+JSON" })
        {
            data.Add(binding, "\"TASK_STATE_FUTURE\"", TaskState.Unspecified);
            data.Add(binding, "99", TaskState.Unspecified);
            data.Add(binding, "3", TaskState.Completed); // a known integer reads as that state
        }
        return data;
    }

    [Theory, MemberData(nameof(StubStates))]
    public async Task Client_reads_a_json_task_state_it_does_not_know_as_unspecified(string binding, string stateJson, TaskState expected)
    {
        var options = new A2AClientOptions { HttpClient = new HttpClient(new StubHandler(binding == "JSONRPC", stateJson)) };
        var card = new AgentCard { Name = "stub" };
        await using IA2AClient client = binding == "JSONRPC"
            ? new JsonRpcA2AClient(new Uri("http://stub.test/jsonrpc"), card, options)
            : new RestA2AClient(new Uri("http://stub.test/agents/x/rest/"), card, options);

        var task = await client.GetTaskAsync(new GetTaskRequest { Id = "t1" }, Ct);

        Assert.Equal(expected, task.Status.State);
    }

    private sealed class StubGrpcService : Proto.A2AService.A2AServiceBase
    {
        public override Task<Proto.Task> GetTask(Proto.GetTaskRequest request, ServerCallContext context)
            => Task.FromResult(new Proto.Task { Id = "t1", ContextId = "c", Status = new Proto.TaskStatus { State = (Proto.TaskState)99 } });
    }

    [Fact]
    public async Task Client_reads_a_grpc_task_state_it_does_not_know_as_unspecified()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        await using var app = builder.Build();
        app.MapGrpcService<StubGrpcService>();
        await app.StartAsync(Ct);
        // GrpcA2AClient is internal; the public way to get one is the transport (same constructor, no tenant).
        var grpcInterface = new AgentInterface { Url = app.Urls.First(), ProtocolBinding = "GRPC", ProtocolVersion = "1.0" };
        await using var client = GrpcClientTransport.Instance.Create(grpcInterface, new AgentCard { Name = "stub" }, new A2AClientOptions(), null);

        var task = await client.GetTaskAsync(new GetTaskRequest { Id = "t1" }, Ct);

        Assert.Equal(TaskState.Unspecified, task.Status.State);
    }
}
