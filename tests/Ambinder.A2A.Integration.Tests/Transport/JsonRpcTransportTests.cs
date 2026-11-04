using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ambinder.A2A.Integration.Tests.Fixtures;

namespace Ambinder.A2A.Integration.Tests.Transport;

[Collection(MinimalServerCollection.Name)]
public sealed class JsonRpcTransportTests(MinimalServer server)
{
    private const string Echo = MinimalServer.EchoAgentName;
    private const string Protected = MinimalServer.ProtectedAgentName;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- cards ----------

    [Fact]
    public async Task Card_is_served_at_root_and_per_agent_without_version_header()
    {
        using var client = server.CreateClient();

        var root = await GetJsonAsync(client, ".well-known/agent-card.json");
        Assert.Equal(Echo, root["name"]!.GetValue<string>());

        var echo = await GetJsonAsync(client, $"agents/{Echo}/.well-known/agent-card.json");
        Assert.Equal(Echo, echo["name"]!.GetValue<string>());

        // Cards are anonymous, even for a protected agent.
        var prot = await GetJsonAsync(client, $"agents/{Protected}/.well-known/agent-card.json");
        Assert.Equal(Protected, prot["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Card_lists_three_interfaces_with_protocol_version_1_0()
    {
        using var client = server.CreateClient();
        var card = await GetJsonAsync(client, $"agents/{Echo}/.well-known/agent-card.json");

        var interfaces = card["supportedInterfaces"]!.AsArray()
            .Select(i => (Url: i!["url"]!.GetValue<string>(), Binding: i["protocolBinding"]!.GetValue<string>(), Version: i["protocolVersion"]!.GetValue<string>()))
            .ToList();

        Assert.Equal(
            [
                ($"{MinimalServer.BaseUrl}/agents/{Echo}/jsonrpc", "JSONRPC", "1.0"),
                ($"{MinimalServer.BaseUrl}/agents/{Echo}/rest", "HTTP+JSON", "1.0"),
                ($"127.0.0.1:{MinimalServer.EchoGrpcPort}", "GRPC", "1.0"),
            ],
            interfaces);
    }

    [Fact]
    public async Task Push_jwks_is_served_without_version_header()
    {
        using var client = server.CreateClient();
        var jwks = await GetJsonAsync(client, ".well-known/a2a-push-jwks.json");
        var key = Assert.Single(jwks["keys"]!.AsArray());
        Assert.Equal("RSA", key!["kty"]!.GetValue<string>());
    }

    // ---------- operations ----------

    [Fact]
    public async Task SendMessage_over_jsonrpc_returns_task_result()
    {
        using var client = server.CreateClient();
        var response = await RpcAsync(client, Echo, Request("req-1", "SendMessage", SendParams("hello")));

        Assert.Equal("req-1", response["id"]!.GetValue<string>());
        Assert.Equal("2.0", response["jsonrpc"]!.GetValue<string>());
        Assert.Null(response["error"]);
        var task = response["result"]!["task"]!;
        Assert.Equal("TASK_STATE_COMPLETED", task["status"]!["state"]!.GetValue<string>());
        Assert.Equal("echo: hello", task["artifacts"]![0]!["parts"]![0]!["text"]!.GetValue<string>());

        // GetTask finds it.
        var id = task["id"]!.GetValue<string>();
        var got = await RpcAsync(client, Echo, Request(7, "GetTask", new JsonObject { ["id"] = id }));
        Assert.Equal(id, got["result"]!["id"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("\"abc\"", "\"abc\"")]
    [InlineData("42", "42")]
    [InlineData("1.5", "1.5")]
    [InlineData("null", "null")]
    [InlineData(null, "null")] // no id → processed, answered with "id": null
    public async Task Jsonrpc_id_is_echoed_exactly(string? idJson, string expected)
    {
        using var client = server.CreateClient();
        var idPart = idJson is null ? "" : $"\"id\":{idJson},";
        var body = $$$"""{"jsonrpc":"2.0",{{{idPart}}}"method":"GetTask","params":{"id":"missing"}}""";
        var response = await RpcAsync(client, Echo, body);
        Assert.Equal(expected, response["id"]?.ToJsonString() ?? "null");
        Assert.True(response.AsObject().ContainsKey("id"));
    }

    [Fact]
    public async Task SendStreamingMessage_over_jsonrpc_emits_jsonrpc_wrapped_sse_frames()
    {
        using var client = server.CreateClient();
        using var request = RpcRequest(Echo, Request(99, "SendStreamingMessage", SendParams("stream me")).ToJsonString());
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache);

        var frames = await ReadSseAsync(response);
        Assert.True(frames.Count >= 2);
        Assert.All(frames, f =>
        {
            Assert.Equal("2.0", f["jsonrpc"]!.GetValue<string>());
            Assert.Equal(99, f["id"]!.GetValue<int>());
            Assert.NotNull(f["result"]);
        });
        Assert.NotNull(frames[0]["result"]!["task"]);
        Assert.Contains(frames, f => f["result"]!["artifactUpdate"]?["artifact"]?["parts"]?[0]?["text"]?.GetValue<string>() == "echo: stream me");
        Assert.Equal("TASK_STATE_COMPLETED", frames[^1]["result"]!["statusUpdate"]!["status"]!["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task Client_disconnect_ends_only_that_subscriber()
    {
        using var client = server.CreateClient();
        var sendParams = SendParams("slow:1500");
        sendParams["configuration"] = new JsonObject { ["returnImmediately"] = true };
        var sent = await RpcAsync(client, Echo, Request(1, "SendMessage", sendParams));
        var taskId = sent["result"]!["task"]!["id"]!.GetValue<string>();
        var subscribe = Request(2, "SubscribeToTask", new JsonObject { ["id"] = taskId }).ToJsonString();

        using var leaver = server.CreateClient();
        using var stayer = server.CreateClient();
        var leaving = await leaver.SendAsync(RpcRequest(Echo, subscribe), HttpCompletionOption.ResponseHeadersRead, Ct);
        using var staying = await stayer.SendAsync(RpcRequest(Echo, subscribe), HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal("text/event-stream", leaving.Content.Headers.ContentType?.MediaType);

        // Read the first frame, then hang up.
        using (var reader = new StreamReader(await leaving.Content.ReadAsStreamAsync(Ct)))
        {
            var first = await reader.ReadLineAsync(Ct);
            Assert.StartsWith("data: ", first);
        }
        leaving.Dispose();
        leaver.CancelPendingRequests();

        var frames = await ReadSseAsync(staying);
        Assert.Equal("TASK_STATE_COMPLETED", frames[^1]["result"]!["statusUpdate"]!["status"]!["state"]!.GetValue<string>());
        var final = await RpcAsync(client, Echo, Request(3, "GetTask", new JsonObject { ["id"] = taskId }));
        Assert.Equal("TASK_STATE_COMPLETED", final["result"]!["status"]!["state"]!.GetValue<string>());
    }

    // Spec 9.1 (TCK JSONRPC-FMT-002): JSON-RPC requests and responses use application/json, results and errors alike.
    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"ListTasks","params":{}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"GetTask","params":{"id":"missing"}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"NoSuchMethod","params":{}}""")]
    public async Task Unary_response_uses_application_json_content_type(string body)
    {
        using var client = server.CreateClient();
        using var response = await client.SendAsync(RpcRequest(Echo, body), Ct);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    // Other SDKs post JSON-RPC bodies as application/a2a+json; the server accepts both types.
    [Fact]
    public async Task Request_posted_as_a2a_json_is_accepted()
    {
        using var client = server.CreateClient();
        using var request = RpcRequest(Echo, """{"jsonrpc":"2.0","id":1,"method":"ListTasks","params":{}}""");
        request.Content!.Headers.ContentType = new("application/a2a+json");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
        Assert.NotNull(body["result"]);
        Assert.Null(body["error"]);
    }

    [Fact]
    public async Task Push_config_roundtrip_and_delete_returns_empty_object()
    {
        using var client = server.CreateClient();
        var sent = await RpcAsync(client, Echo, Request(1, "SendMessage", SendParams("push")));
        var taskId = sent["result"]!["task"]!["id"]!.GetValue<string>();

        var created = await RpcAsync(client, Echo, Request(2, "CreateTaskPushNotificationConfig",
            new JsonObject { ["taskId"] = taskId, ["url"] = "http://127.0.0.1:5300/hook" }));
        var configId = created["result"]!["id"]!.GetValue<string>();

        var list = await RpcAsync(client, Echo, Request(3, "ListTaskPushNotificationConfigs", new JsonObject { ["taskId"] = taskId }));
        Assert.Single(list["result"]!["configs"]!.AsArray());

        var deleted = await RpcAsync(client, Echo, Request(4, "DeleteTaskPushNotificationConfig",
            new JsonObject { ["taskId"] = taskId, ["id"] = configId }));
        Assert.Equal("{}", deleted["result"]!.ToJsonString());
    }

    // ---------- service parameters ----------

    [Theory]
    [InlineData(null)]
    [InlineData("0.3")]
    [InlineData("2.0")]
    public async Task Missing_version_header_returns_version_not_supported(string? version)
    {
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"agents/{Echo}/jsonrpc")
        {
            Content = new StringContent(Request(5, "GetTask", new JsonObject { ["id"] = "x" }).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (version is not null) request.Headers.Add("A2A-Version", version);
        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal(5, body["id"]!.GetValue<int>());
        Assert.Equal(-32009, body["error"]!["code"]!.GetValue<int>());
        // Spec 9.5: data is an array of @type-tagged details holding the ErrorInfo.
        Assert.Equal(
            """[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"VERSION_NOT_SUPPORTED","domain":"a2a-protocol.org","metadata":{"supportedVersions":"1.0"}}]""",
            body["error"]!["data"]!.ToJsonString());
    }

    [Theory]
    [InlineData("1.0.1")]
    [InlineData(" 1.0 ")]
    public async Task Version_patch_is_ignored(string version)
    {
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"agents/{Echo}/jsonrpc")
        {
            Content = new StringContent(Request(5, "SendMessage", SendParams("v")).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("A2A-Version", version);
        using var response = await client.SendAsync(request, Ct);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
        Assert.Null(body["error"]);
        Assert.NotNull(body["result"]);
    }

    [Fact]
    public async Task Standard_jsonrpc_errors_have_no_data()
    {
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"agents/{Echo}/jsonrpc")
        {
            Content = new StringContent(Request(7, "NoSuchMethod", new JsonObject()).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("A2A-Version", "1.0");
        using var response = await client.SendAsync(request, Ct);

        var error = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["error"]!;
        Assert.Equal(-32601, error["code"]!.GetValue<int>());
        Assert.Null(error["data"]);
    }

    [Fact]
    public async Task A2A_errors_carry_error_info_in_data()
    {
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"agents/{Echo}/jsonrpc")
        {
            Content = new StringContent(Request(8, "GetTask", new JsonObject { ["id"] = "missing" }).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("A2A-Version", "1.0");
        using var response = await client.SendAsync(request, Ct);

        var error = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["error"]!;
        Assert.Equal(-32001, error["code"]!.GetValue<int>());
        var detail = Assert.Single(error["data"]!.AsArray())!;
        Assert.Equal("type.googleapis.com/google.rpc.ErrorInfo", detail["@type"]!.GetValue<string>());
        Assert.Equal("TASK_NOT_FOUND", detail["reason"]!.GetValue<string>());
        Assert.Equal("a2a-protocol.org", detail["domain"]!.GetValue<string>());
    }

    [Fact]
    public async Task Version_as_query_parameter_is_accepted()
    {
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"agents/{Echo}/jsonrpc?A2A-Version=1.0")
        {
            Content = new StringContent(Request(6, "SendMessage", SendParams("q")).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request, Ct);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
        Assert.Null(body["error"]);
        Assert.Equal("TASK_STATE_COMPLETED", body["result"]!["task"]!["status"]!["state"]!.GetValue<string>());
    }

    // ---------- errors (Review Focus 3) ----------

    public static TheoryData<string, int> MalformedBodies => new()
    {
        { "{", -32700 },
        { "not json at all", -32700 },
        { "", -32700 },
        { """{"jsonrpc":"1.0","id":1,"method":"GetTask","params":{"id":"x"}}""", -32600 },
        { """{"id":1,"method":"GetTask","params":{"id":"x"}}""", -32600 },
        { """{"jsonrpc":"2.0","id":1,"params":{}}""", -32600 },
        { """{"jsonrpc":"2.0","id":1,"method":5}""", -32600 },
        { """{"jsonrpc":"2.0","id":{"a":1},"method":"GetTask"}""", -32600 },
        { """[{"jsonrpc":"2.0","id":1,"method":"GetTask","params":{"id":"x"}}]""", -32600 },
        { "42", -32600 },
        { """{"jsonrpc":"2.0","id":1,"method":"NoSuchMethod","params":{}}""", -32601 },
        { """{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"messageId":"m1","role":"ROLE_USER","parts":[{"text":"a","url":"http://x"}]}}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"messageId":"m1","role":"ROLE_USER","parts":[{}]}}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"messageId":"m1","role":"ROLE_BOGUS","parts":[{"text":"a"}]}}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"messageId":"m1","role":0,"parts":[{"text":"a"}]}}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"messageId":"m1","role":"ROLE_USER","parts":[{"raw":"%%%"}]}}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"GetTask","params":{}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"GetTask","params":{"id":""}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"CancelTask","params":{}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"SubscribeToTask","params":{}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"SendStreamingMessage","params":{}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"CreateTaskPushNotificationConfig","params":{"url":"http://x"}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"GetTaskPushNotificationConfig","params":{"taskId":"t"}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"GetTaskPushNotificationConfig","params":{"id":"c"}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"ListTaskPushNotificationConfigs","params":{}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"DeleteTaskPushNotificationConfig","params":{"id":"c"}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"role":"ROLE_USER","parts":[{"text":"a"}]}}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"messageId":"m1","role":"ROLE_USER","parts":[]}}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"GetTask","params":[1,2]}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"GetTask","params":"x"}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"ListTasks","params":{"status":true}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"ListTasks","params":{"pageSize":"ten"}}""", -32602 },
        { """{"jsonrpc":"2.0","id":1,"method":"SendStreamingMessage","params":{"message":{"messageId":"m1","role":"ROLE_USER","parts":[{"text":"a","data":{}}]}}}""", -32602 },
    };

    [Theory]
    [MemberData(nameof(MalformedBodies))]
    public async Task Malformed_requests_map_to_jsonrpc_errors(string body, int expectedCode)
    {
        using var client = server.CreateClient();
        using var response = await client.SendAsync(RpcRequest(Echo, body), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("2.0", json["jsonrpc"]!.GetValue<string>());
        Assert.Equal(expectedCode, json["error"]!["code"]!.GetValue<int>());
        Assert.False(string.IsNullOrEmpty(json["error"]!["message"]!.GetValue<string>()));
        Assert.Null(json["result"]);
    }

    [Fact]
    public async Task Body_over_limit_returns_413()
    {
        using var client = server.CreateClient();
        var text = new string('a', 4 * 1024 * 1024 + 16);
        var body = Request(1, "SendMessage", SendParams(text)).ToJsonString();
        using var response = await client.SendAsync(RpcRequest(Echo, body), Ct);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Chunked_body_over_limit_returns_413()
    {
        using var client = server.CreateClient();
        var bytes = Encoding.UTF8.GetBytes(Request(1, "SendMessage", SendParams(new string('a', 4 * 1024 * 1024 + 16))).ToJsonString());
        using var request = new HttpRequestMessage(HttpMethod.Post, $"agents/{Echo}/jsonrpc") { Content = new StreamContent(new UnknownLengthStream(bytes)) };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("A2A-Version", "1.0");
        request.Headers.TransferEncodingChunked = true;
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task A2A_errors_map_to_jsonrpc_codes()
    {
        using var client = server.CreateClient();

        var notFound = await RpcAsync(client, Echo, Request("e1", "GetTask", new JsonObject { ["id"] = "no-such-task" }));
        Assert.Equal("e1", notFound["id"]!.GetValue<string>());
        Assert.Equal(-32001, notFound["error"]!["code"]!.GetValue<int>());

        var cancel = await RpcAsync(client, Echo, Request("e2", "CancelTask", new JsonObject { ["id"] = "no-such-task" }));
        Assert.Equal(-32001, cancel["error"]!["code"]!.GetValue<int>());

        var subscribe = await RpcAsync(client, Echo, Request("e3", "SubscribeToTask", new JsonObject { ["id"] = "no-such-task" }));
        Assert.Equal(-32001, subscribe["error"]!["code"]!.GetValue<int>());

        // Completed task cannot be canceled.
        var sent = await RpcAsync(client, Echo, Request(1, "SendMessage", SendParams("done")));
        var id = sent["result"]!["task"]!["id"]!.GetValue<string>();
        var notCancelable = await RpcAsync(client, Echo, Request("e4", "CancelTask", new JsonObject { ["id"] = id }));
        Assert.Equal(-32002, notCancelable["error"]!["code"]!.GetValue<int>());
    }

    // ---------- routing and auth ----------

    [Fact]
    public async Task Unknown_agent_returns_404()
    {
        using var client = server.CreateClient();
        using var rpc = await client.SendAsync(RpcRequest("nope", Request(1, "GetTask", new JsonObject { ["id"] = "x" }).ToJsonString()), Ct);
        Assert.Equal(HttpStatusCode.NotFound, rpc.StatusCode);

        using var card = await client.GetAsync("agents/nope/.well-known/agent-card.json", Ct);
        Assert.Equal(HttpStatusCode.NotFound, card.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_call_to_protected_agent_returns_401()
    {
        var body = Request(1, "SendMessage", SendParams("secret")).ToJsonString();

        using (var anonymous = server.CreateClient())
        {
            using var response = await anonymous.SendAsync(RpcRequest(Protected, body), Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var noRole = server.CreateClient("bob"))
        {
            using var response = await noRole.SendAsync(RpcRequest(Protected, body), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using (var invoker = server.CreateClient("alice", MinimalServer.InvokerRole))
        {
            var json = await RpcAsync(invoker, Protected, body);
            Assert.Equal("TASK_STATE_COMPLETED", json["result"]!["task"]!["status"]!["state"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Extended_card_requires_an_authenticated_caller()
    {
        var body = Request(1, "GetExtendedAgentCard", new JsonObject()).ToJsonString();

        using (var anonymous = server.CreateClient())
        {
            using var response = await anonymous.SendAsync(RpcRequest(Echo, body), Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var user = server.CreateClient("carol");
        var json = await RpcAsync(user, Echo, body);
        Assert.Equal("Extended echo card", json["result"]!["description"]!.GetValue<string>());
        Assert.Equal(3, json["result"]!["supportedInterfaces"]!.AsArray().Count);
    }

    [Fact]
    public async Task Tasks_are_owned_by_the_caller()
    {
        using var alice = server.CreateClient("alice-owner");
        using var mallory = server.CreateClient("mallory");
        var sent = await RpcAsync(alice, Echo, Request(1, "SendMessage", SendParams("mine")));
        var id = sent["result"]!["task"]!["id"]!.GetValue<string>();

        var other = await RpcAsync(mallory, Echo, Request(2, "GetTask", new JsonObject { ["id"] = id }));
        Assert.Equal(-32001, other["error"]!["code"]!.GetValue<int>());
    }

    // ---------- helpers ----------

    private static JsonObject Request(JsonNode? id, string method, JsonNode @params) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = method,
        ["params"] = @params,
    };

    private static JsonObject SendParams(string text) => new()
    {
        ["message"] = new JsonObject
        {
            ["messageId"] = Guid.NewGuid().ToString(),
            ["role"] = "ROLE_USER",
            ["parts"] = new JsonArray(new JsonObject { ["text"] = text }),
        },
    };

    private static HttpRequestMessage RpcRequest(string agent, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"agents/{agent}/jsonrpc")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("A2A-Version", "1.0");
        return request;
    }

    private static Task<JsonNode> RpcAsync(HttpClient client, string agent, JsonObject request) => RpcAsync(client, agent, request.ToJsonString());

    private static async Task<JsonNode> RpcAsync(HttpClient client, string agent, string body)
    {
        using var response = await client.SendAsync(RpcRequest(agent, body), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
    }

    private static async Task<JsonNode> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
    }

    private static async Task<List<JsonNode>> ReadSseAsync(HttpResponseMessage response)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var frames = new List<JsonNode>();
        while (await reader.ReadLineAsync(cts.Token) is { } line)
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
                frames.Add(JsonNode.Parse(line["data: ".Length..])!);
        }
        return frames;
    }

    /// <summary>A stream whose length is unknown, so HttpClient sends it chunked.</summary>
    private sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => base.Position; set => throw new NotSupportedException(); }
    }
}
