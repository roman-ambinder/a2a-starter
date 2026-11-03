using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using A2A.Integration.Tests.Fixtures;

namespace A2A.Integration.Tests.Transport;

[Collection(MinimalServerCollection.Name)]
public sealed class RestTransportTests(MinimalServer server)
{
    private const string Echo = MinimalServer.EchoAgentName;
    private const string Protected = MinimalServer.ProtectedAgentName;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- routes ----------

    public static TheoryData<string, string, string> Operations => new()
    {
        { "POST", "message:send", "send" },
        { "POST", "message:stream", "send" },
        { "GET", "tasks/{task}", "" },
        { "GET", "tasks", "" },
        { "POST", "tasks/{task}:cancel", "" },
        { "GET", "tasks/{task}:subscribe", "" },
        { "POST", "tasks/{task}/pushNotificationConfigs", "push" },
        { "GET", "tasks/{task}/pushNotificationConfigs/{config}", "" },
        { "GET", "tasks/{task}/pushNotificationConfigs", "" },
        { "DELETE", "tasks/{task}/pushNotificationConfigs/{config}", "" },
        { "GET", "extendedAgentCard", "" },
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Every_operation_is_reachable_at_spec_path(string method, string template, string bodyKind)
    {
        using var client = Client("dave");
        var (task, config) = await SetUpAsync(client);
        var path = template.Replace("{task}", task).Replace("{config}", config);
        var body = bodyKind switch
        {
            "send" => SendBody("reach").ToJsonString(),
            "push" => new JsonObject { ["url"] = "http://127.0.0.1:9/other" }.ToJsonString(),
            _ => null,
        };

        using var response = await client.SendAsync(Req(new HttpMethod(method), Echo, path, body), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        // The task is already COMPLETED: cancel is refused; subscribe may be refused. Everything else succeeds.
        if (template.EndsWith(":cancel", StringComparison.Ordinal))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("TASK_NOT_CANCELABLE", JsonNode.Parse(text)!["error"]!["details"]![0]!["reason"]!.GetValue<string>());
        }
        else if (template.EndsWith(":subscribe", StringComparison.Ordinal))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("UNSUPPORTED_OPERATION", JsonNode.Parse(text)!["error"]!["details"]![0]!["reason"]!.GetValue<string>());
        }
        else if (method == "DELETE")
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        else
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {text}");
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Every_operation_is_reachable_at_tenant_prefixed_path_and_passes_the_tenant(string method, string template, string bodyKind)
    {
        using var client = Client("dave");
        var (task, config) = await SetUpAsync(client);
        var tenant = "acme-" + Guid.NewGuid().ToString("N")[..8];
        var path = $"{tenant}/" + template.Replace("{task}", task).Replace("{config}", config);
        var body = bodyKind switch
        {
            "send" => SendBody("reach").ToJsonString(),
            "push" => new JsonObject { ["url"] = "http://127.0.0.1:9/other" }.ToJsonString(),
            _ => null,
        };

        using var response = await client.SendAsync(Req(new HttpMethod(method), Echo, path, body), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.True((int)response.StatusCode < 500, $"{response.StatusCode}: {text}");
        Assert.Contains(server.HandlerCalls, c => c.Tenant == tenant);
    }

    [Fact]
    public async Task Tenant_prefixed_send_and_get_roundtrip()
    {
        using var client = Client("erin");
        var body = SendBody("tenant hello");
        body["tenant"] = "acme"; // same as the path: accepted
        using var sent = await client.SendAsync(Req(HttpMethod.Post, Echo, "acme/message:send", body.ToJsonString()), Ct);
        var text = await sent.Content.ReadAsStringAsync(Ct);
        Assert.True(sent.StatusCode == HttpStatusCode.OK, text);
        var id = JsonNode.Parse(text)!["task"]!["id"]!.GetValue<string>();

        var got = await GetAsync(client, $"acme/tasks/{id}");
        Assert.Equal(id, got["id"]!.GetValue<string>());
        var listed = await GetAsync(client, "acme/tasks");
        Assert.Contains(listed["tasks"]!.AsArray(), t => t!["id"]!.GetValue<string>() == id);
    }

    [Fact]
    public async Task Tenant_in_body_that_differs_from_path_is_invalid_params()
    {
        using var client = Client("erin");
        var body = SendBody("x");
        body["tenant"] = "other";
        using var response = await client.SendAsync(Req(HttpMethod.Post, Echo, "acme/message:send", body.ToJsonString()), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["error"]!;
        Assert.Equal("INVALID_ARGUMENT", error["status"]!.GetValue<string>());
        Assert.Contains("tenant", error["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Send_and_get_task_roundtrip()
    {
        using var client = Client("erin");
        using var sent = await client.SendAsync(Req(HttpMethod.Post, Echo, "message:send", SendBody("hello").ToJsonString()), Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Equal("application/a2a+json", sent.Content.Headers.ContentType?.MediaType);
        var json = JsonNode.Parse(await sent.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("TASK_STATE_COMPLETED", json["task"]!["status"]!["state"]!.GetValue<string>());
        var id = json["task"]!["id"]!.GetValue<string>();

        var got = await GetAsync(client, $"tasks/{id}?historyLength=0");
        Assert.Equal(id, got["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task List_tasks_query_parameters_bind()
    {
        using var client = Client("lister-" + Guid.NewGuid().ToString("N"));
        for (var i = 0; i < 3; i++) await SendAsync(client, $"list {i}");

        var page = await GetAsync(client, "tasks?status=TASK_STATE_COMPLETED&pageSize=2&includeArtifacts=true");
        var tasks = page["tasks"]!.AsArray();
        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, t => Assert.NotNull(t!["artifacts"]));
        Assert.False(string.IsNullOrEmpty(page["nextPageToken"]?.GetValue<string>()));

        var next = await GetAsync(client, $"tasks?status=TASK_STATE_COMPLETED&pageSize=2&pageToken={Uri.EscapeDataString(page["nextPageToken"]!.GetValue<string>())}");
        Assert.Single(next["tasks"]!.AsArray());
        // Spec 3.1.4: nextPageToken is always present, "" on the last page; tasks newest first.
        Assert.Equal("", next["nextPageToken"]!.GetValue<string>());
        var stamps = tasks.Concat(next["tasks"]!.AsArray()).Select(t => DateTimeOffset.Parse(t!["status"]!["timestamp"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(stamps.OrderByDescending(x => x), stamps);

        var without = await GetAsync(client, "tasks?pageSize=1&statusTimestampAfter=2000-01-01T00:00:00Z");
        Assert.Null(without["tasks"]![0]!["artifacts"]);

        var none = await GetAsync(client, "tasks?status=TASK_STATE_FAILED");
        Assert.Empty(none["tasks"]?.AsArray() ?? []);
    }

    [Theory]
    [InlineData("tasks?pageSize=ten")]
    [InlineData("tasks?includeArtifacts=maybe")]
    [InlineData("tasks?statusTimestampAfter=yesterday")]
    [InlineData("tasks?historyLength=1.5")]
    [InlineData("tasks/x?historyLength=abc")]
    public async Task Invalid_query_values_are_invalid_params(string path)
    {
        using var client = Client("frank");
        using var response = await client.SendAsync(Req(HttpMethod.Get, Echo, path, null), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("INVALID_ARGUMENT", body["error"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Subscribe_accepts_get_and_post()
    {
        using var client = Client("gina");
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post })
        {
            var id = await SendAsync(client, "slow:600", returnImmediately: true);
            using var response = await client.SendAsync(Req(method, Echo, $"tasks/{id}:subscribe", null), HttpCompletionOption.ResponseHeadersRead, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            var frames = await ReadSseAsync(response);
            Assert.NotNull(frames[0]["task"]);
            Assert.Equal("TASK_STATE_COMPLETED", frames[^1]["statusUpdate"]!["status"]!["state"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Stream_frames_are_bare_stream_responses()
    {
        using var client = Client("hank");
        using var response = await client.SendAsync(Req(HttpMethod.Post, Echo, "message:stream", SendBody("stream me").ToJsonString()), HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache);

        var frames = await ReadSseAsync(response);
        Assert.True(frames.Count >= 2);
        Assert.All(frames, f =>
        {
            Assert.Null(f["jsonrpc"]);
            Assert.Null(f["result"]);
            Assert.Single(f.AsObject());
        });
        Assert.NotNull(frames[0]["task"]);
        Assert.Contains(frames, f => f["artifactUpdate"]?["artifact"]?["parts"]?[0]?["text"]?.GetValue<string>() == "echo: stream me");
        Assert.Equal("TASK_STATE_COMPLETED", frames[^1]["statusUpdate"]!["status"]!["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task Pre_stream_error_is_a_plain_error_response()
    {
        using var client = Client("ida");
        using var response = await client.SendAsync(Req(HttpMethod.Get, Echo, "tasks/missing:subscribe", null), Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/a2a+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("TASK_NOT_FOUND", JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["error"]!["details"]![0]!["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task Cancel_accepts_empty_body_and_cancels_running_task()
    {
        using var client = Client("jo");
        var id = await SendAsync(client, "slow:5000", returnImmediately: true);
        using var response = await client.SendAsync(Req(HttpMethod.Post, Echo, $"tasks/{id}:cancel", null), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("TASK_STATE_CANCELED", json["status"]!["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task Push_config_crud_and_path_task_id_wins()
    {
        using var client = Client("kim");
        var task = await SendAsync(client, "push");

        using var created = await client.SendAsync(Req(HttpMethod.Post, Echo, $"tasks/{task}/pushNotificationConfigs",
            new JsonObject { ["url"] = "http://127.0.0.1:9/hook" }.ToJsonString()), Ct);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var cfg = JsonNode.Parse(await created.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal(task, cfg["taskId"]!.GetValue<string>());
        var id = cfg["id"]!.GetValue<string>();

        var list = await GetAsync(client, $"tasks/{task}/pushNotificationConfigs");
        Assert.Single(list["configs"]!.AsArray());
        var one = await GetAsync(client, $"tasks/{task}/pushNotificationConfigs/{id}");
        Assert.Equal(id, one["id"]!.GetValue<string>());

        using var mismatch = await client.SendAsync(Req(HttpMethod.Post, Echo, $"tasks/{task}/pushNotificationConfigs",
            new JsonObject { ["taskId"] = "other", ["url"] = "http://127.0.0.1:9/hook" }.ToJsonString()), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

        using var deleted = await client.SendAsync(Req(HttpMethod.Delete, Echo, $"tasks/{task}/pushNotificationConfigs/{id}", null), Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal("", await deleted.Content.ReadAsStringAsync(Ct));
    }

    // ---------- errors ----------

    [Fact]
    public async Task Error_body_is_google_rpc_status_with_error_info()
    {
        using var client = Client("lee");
        using var response = await client.SendAsync(Req(HttpMethod.Get, Echo, "tasks/missing", null), Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["error"]!;
        Assert.Equal(404, error["code"]!.GetValue<int>());
        Assert.Equal("NOT_FOUND", error["status"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(error["message"]!.GetValue<string>()));
        var info = error["details"]![0]!;
        Assert.Equal("type.googleapis.com/google.rpc.ErrorInfo", info["@type"]!.GetValue<string>());
        Assert.Equal("TASK_NOT_FOUND", info["reason"]!.GetValue<string>());
        Assert.Equal("a2a-protocol.org", info["domain"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.3")]
    public async Task Version_not_supported_is_400_failed_precondition(string? version)
    {
        using var client = Client("max");
        using var request = Req(HttpMethod.Get, Echo, "tasks/x", null, version: false);
        if (version is not null) request.Headers.Add("A2A-Version", version);
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["error"]!;
        Assert.Equal("FAILED_PRECONDITION", error["status"]!.GetValue<string>());
        Assert.Equal("VERSION_NOT_SUPPORTED", error["details"]![0]!["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task Version_as_query_parameter_is_accepted_and_not_an_operation_parameter()
    {
        using var client = Client("nan");
        using var request = Req(HttpMethod.Get, Echo, "tasks?A2A-Version=1.0&pageSize=1", null, version: false);
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"message\":{\"messageId\":\"m\",\"role\":\"ROLE_BOGUS\",\"parts\":[{\"text\":\"a\"}]}}")]
    [InlineData("{}")]
    [InlineData("{\"message\":{}}")]
    [InlineData("{\"message\":{\"role\":\"ROLE_USER\",\"parts\":[{\"text\":\"a\"}]}}")]
    [InlineData("{\"message\":{\"messageId\":\"m\",\"role\":\"ROLE_USER\",\"parts\":[]}}")]
    public async Task Bad_send_bodies_are_400(string body)
    {
        using var client = Client("ova");
        using var response = await client.SendAsync(Req(HttpMethod.Post, Echo, "message:send", body), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["error"]);
    }

    [Fact]
    public async Task Missing_message_is_invalid_params_not_a_server_error()
    {
        using var client = Client("pat");
        using var response = await client.SendAsync(Req(HttpMethod.Post, Echo, "message:stream", "{}"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_ARGUMENT", JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["error"]!["status"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("GET", "tasks/a%3Ab")]
    [InlineData("POST", "tasks/a:b:cancel")]
    [InlineData("POST", "tasks/a%3Ab%3Acancel")]
    [InlineData("GET", "tasks/a:b:subscribe")]
    public async Task Task_ids_with_colon_are_parsed(string method, string path)
    {
        using var client = Client("rho");
        using var response = await client.SendAsync(Req(new HttpMethod(method), Echo, path, null), Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["error"]!;
        Assert.Equal("TASK_NOT_FOUND", error["details"]![0]!["reason"]!.GetValue<string>());
        Assert.Contains("'a:b'", error["message"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("tasks/")]
    [InlineData("tasks")]
    public async Task Trailing_slash_is_accepted(string path)
    {
        using var client = Client("sol");
        using var response = await client.SendAsync(Req(HttpMethod.Get, Echo, path, null), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_path_and_wrong_verb_are_rejected()
    {
        using var client = Client("quin");
        using var unknown = await client.SendAsync(Req(HttpMethod.Get, Echo, "nothing/here", null), Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var wrong = await client.SendAsync(Req(HttpMethod.Delete, Echo, "tasks/x", null), Ct);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrong.StatusCode);
    }

    [Fact]
    public async Task Body_over_limit_returns_413()
    {
        using var client = Client("rae");
        var body = SendBody(new string('a', 4 * 1024 * 1024 + 16)).ToJsonString();
        using var response = await client.SendAsync(Req(HttpMethod.Post, Echo, "message:send", body), Ct);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_agent_returns_404()
    {
        using var client = Client("sam");
        using var response = await client.SendAsync(Req(HttpMethod.Get, "nope", "tasks", null), Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Protected_agent_requires_authentication_and_role()
    {
        using (var anonymous = Client(null))
        {
            using var response = await anonymous.SendAsync(Req(HttpMethod.Get, Protected, "tasks", null), Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using (var noRole = Client("bob"))
        {
            using var response = await noRole.SendAsync(Req(HttpMethod.Get, Protected, "tasks", null), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using var invoker = Client("alice", MinimalServer.InvokerRole);
        using var ok = await invoker.SendAsync(Req(HttpMethod.Get, Protected, "tasks", null), Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Extended_card_requires_an_authenticated_caller()
    {
        using (var anonymous = Client(null))
        {
            using var response = await anonymous.SendAsync(Req(HttpMethod.Get, Echo, "extendedAgentCard", null), Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var user = Client("tom");
        var card = await GetAsync(user, "extendedAgentCard");
        Assert.Equal("Extended echo card", card["description"]!.GetValue<string>());
    }

    [Fact]
    public async Task Tasks_are_owned_by_the_caller()
    {
        using var alice = Client("alice-rest-owner");
        using var mallory = Client("mallory-rest");
        var id = await SendAsync(alice, "mine");
        using var response = await mallory.SendAsync(Req(HttpMethod.Get, Echo, $"tasks/{id}", null), Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- helpers ----------

    private HttpClient Client(string? user, params string[] roles) => server.CreateClient(user, roles);

    private static HttpRequestMessage Req(HttpMethod method, string agent, string path, string? body, bool version = true)
    {
        var request = new HttpRequestMessage(method, $"agents/{agent}/rest/{path}");
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/a2a+json");
        if (version) request.Headers.Add("A2A-Version", "1.0");
        return request;
    }

    private static JsonObject SendBody(string text, bool returnImmediately = false)
    {
        var body = new JsonObject
        {
            ["message"] = new JsonObject
            {
                ["messageId"] = Guid.NewGuid().ToString(),
                ["role"] = "ROLE_USER",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = text }),
            },
        };
        if (returnImmediately) body["configuration"] = new JsonObject { ["returnImmediately"] = true };
        return body;
    }

    private static async Task<string> SendAsync(HttpClient client, string text, bool returnImmediately = false)
    {
        using var response = await client.SendAsync(Req(HttpMethod.Post, Echo, "message:send", SendBody(text, returnImmediately).ToJsonString()), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["task"]!["id"]!.GetValue<string>();
    }

    private static async Task<(string Task, string Config)> SetUpAsync(HttpClient client)
    {
        var task = await SendAsync(client, "setup");
        using var created = await client.SendAsync(Req(HttpMethod.Post, Echo, $"tasks/{task}/pushNotificationConfigs",
            new JsonObject { ["url"] = "http://127.0.0.1:9/hook" }.ToJsonString()), Ct);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return (task, JsonNode.Parse(await created.Content.ReadAsStringAsync(Ct))!["id"]!.GetValue<string>());
    }

    private static async Task<JsonNode> GetAsync(HttpClient client, string path)
    {
        using var response = await client.SendAsync(Req(HttpMethod.Get, Echo, path, null), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        return JsonNode.Parse(text)!;
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
}
