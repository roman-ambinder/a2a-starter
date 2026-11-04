using System.Text;
using System.Text.Json.Nodes;
using Ambinder.A2A.Client;
using Ambinder.A2A.Integration.Tests.Fixtures;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Integration.Tests.Conformance;

/// <summary>
/// Design D6 on the wire: <c>nextPageToken</c> is <c>""</c> on the last page, tasks come newest first, <c>artifacts</c> is absent
/// unless <c>includeArtifacts</c>, and every listing is scoped to the caller before any filter. Each test uses a fresh user,
/// so its listing holds only its own tasks.
/// </summary>
[Collection(MinimalServerCollection.Name)]
public sealed class ListTasksTests(MinimalServer server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<IA2AClient> ClientAsync(string binding, string user)
    {
        using var http = new HttpClient();
        var card = await new A2ACardResolver(http).GetAgentCardAsync(new Uri($"{MinimalServer.BaseUrl}/agents/{MinimalServer.EchoAgentName}"), Ct);
        return A2AClientFactory.Create(card, new A2AClientOptions
        {
            PreferredBinding = binding,
            Credentials = new RawHeaderCredentials(MinimalServer.UserHeader, user),
        });
    }

    private static string NewUser() => "lister-" + Guid.NewGuid().ToString("N");

    private static SendMessageRequest Send(string text) => new()
    {
        Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText(text)] },
    };

    /// <summary>Creates <paramref name="count"/> completed echo tasks, oldest first; returns their ids.</summary>
    private static async Task<List<string>> SeedAsync(IA2AClient client, int count)
    {
        var ids = new List<string>();
        for (var i = 0; i < count; i++) ids.Add((await client.SendMessageAsync(Send($"task {i}"), Ct)).Task!.Id);
        return ids;
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Pages_newest_first_and_last_page_token_is_empty(string binding)
    {
        await using var client = await ClientAsync(binding, NewUser());
        var ids = await SeedAsync(client, 3);

        var first = await client.ListTasksAsync(new ListTasksRequest { PageSize = 2 }, Ct);
        Assert.Equal(2, first.Tasks.Count);
        Assert.False(string.IsNullOrEmpty(first.NextPageToken));
        var last = await client.ListTasksAsync(new ListTasksRequest { PageSize = 2, PageToken = first.NextPageToken }, Ct);

        Assert.Single(last.Tasks);
        Assert.True(string.IsNullOrEmpty(last.NextPageToken));
        var listed = first.Tasks.Concat(last.Tasks).ToList();
        Assert.Equal(Enumerable.Reverse(ids), listed.Select(t => t.Id));
        var stamps = listed.Select(t => t.Status.Timestamp!.Value).ToList();
        Assert.Equal(stamps.OrderByDescending(t => t), stamps);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Artifacts_are_left_out_unless_requested(string binding)
    {
        await using var client = await ClientAsync(binding, NewUser());
        await SeedAsync(client, 1);

        var without = await client.ListTasksAsync(new ListTasksRequest(), Ct);
        var explicitFalse = await client.ListTasksAsync(new ListTasksRequest { IncludeArtifacts = false }, Ct);
        var with = await client.ListTasksAsync(new ListTasksRequest { IncludeArtifacts = true }, Ct);

        Assert.Empty(Assert.Single(without.Tasks).Artifacts);
        Assert.Empty(Assert.Single(explicitFalse.Tasks).Artifacts);
        Assert.NotEmpty(Assert.Single(with.Tasks).Artifacts);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task History_length_zero_omits_history(string binding)
    {
        await using var client = await ClientAsync(binding, NewUser());
        await SeedAsync(client, 1);

        var full = await client.ListTasksAsync(new ListTasksRequest(), Ct);
        var none = await client.ListTasksAsync(new ListTasksRequest { HistoryLength = 0 }, Ct);

        Assert.NotEmpty(Assert.Single(full.Tasks).History);
        Assert.Empty(Assert.Single(none.Tasks).History);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Listing_is_scoped_to_the_caller_with_or_without_a_context_filter(string binding)
    {
        await using var alice = await ClientAsync(binding, NewUser());
        await using var bob = await ClientAsync(binding, NewUser());
        var aliceTask = (await alice.SendMessageAsync(Send("mine"), Ct)).Task!;
        await SeedAsync(bob, 1);

        var all = await bob.ListTasksAsync(new ListTasksRequest(), Ct);
        var byContext = await bob.ListTasksAsync(new ListTasksRequest { ContextId = aliceTask.ContextId }, Ct);

        Assert.DoesNotContain(all.Tasks, t => t.Id == aliceTask.Id);
        Assert.Single(all.Tasks);
        Assert.Empty(byContext.Tasks);
    }

    [Theory]
    [InlineData("jsonrpc")]
    [InlineData("rest")]
    public async Task Json_wire_has_empty_next_page_token_and_no_artifacts_key(string transport)
    {
        var user = NewUser();
        await using var seeder = await ClientAsync("JSONRPC", user);
        await SeedAsync(seeder, 1);
        using var http = server.CreateClient(user);

        using var request = transport == "jsonrpc"
            ? new HttpRequestMessage(HttpMethod.Post, $"agents/echo/jsonrpc")
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ListTasks","params":{}}""", Encoding.UTF8, "application/json"),
            }
            : new HttpRequestMessage(HttpMethod.Get, "agents/echo/rest/tasks");
        request.Headers.Add(A2AConstants.VersionHeader, "1.0");
        using var response = await http.SendAsync(request, Ct);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
        var list = transport == "jsonrpc" ? body["result"]! : body;

        Assert.Equal("", list["nextPageToken"]!.GetValue<string>());
        var task = Assert.Single(list["tasks"]!.AsArray())!.AsObject();
        Assert.False(task.ContainsKey("artifacts"));
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Page_size_out_of_range_is_invalid_params_and_unset_is_capped(string binding)
    {
        await using var client = await ClientAsync(binding, NewUser());
        await SeedAsync(client, 3);

        foreach (var size in new[] { 0, 101 })
        {
            var error = await Assert.ThrowsAsync<A2AException>(() => client.ListTasksAsync(new ListTasksRequest { PageSize = size }, Ct));
            Assert.Equal(A2AErrorCode.InvalidParams, error.Code);
            Assert.Contains("pageSize must be between 1 and 100", error.Message, StringComparison.Ordinal);
        }
        var unset = await client.ListTasksAsync(new ListTasksRequest(), Ct);
        Assert.InRange(unset.Tasks.Count, 1, 50);
        Assert.InRange(unset.PageSize, 1, 50);
        var max = await client.ListTasksAsync(new ListTasksRequest { PageSize = 100 }, Ct);
        Assert.Equal(3, max.Tasks.Count);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Tasks_are_ordered_by_status_timestamp_descending_then_id(string binding)
    {
        await using var client = await ClientAsync(binding, NewUser());
        var t0 = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var made = new List<(string Id, DateTimeOffset At)>();
        try
        {
            // Created out of time order, so the creation order cannot explain the listing order.
            foreach (var offset in new[] { 30, 10, 20, 20 })
            {
                var at = t0.AddSeconds(offset);
                server.Clock.Freeze(at);
                made.Add(((await client.SendMessageAsync(Send($"at {offset}"), Ct)).Task!.Id, at));
            }
        }
        finally { server.Clock.Release(); }

        var listed = (await client.ListTasksAsync(new ListTasksRequest(), Ct)).Tasks;

        var expected = made.OrderByDescending(m => m.At).ThenByDescending(m => m.Id, StringComparer.Ordinal).Select(m => m.Id);
        Assert.Equal(expected, listed.Select(t => t.Id));
        Assert.Equal(made.Select(m => m.At).OrderByDescending(a => a), listed.Select(t => t.Status.Timestamp!.Value));
    }

    [Theory]
    [InlineData("jsonrpc")]
    [InlineData("rest")]
    public async Task Json_wire_history_and_artifacts_keys_follow_the_request(string transport)
    {
        var user = NewUser();
        await using var seeder = await ClientAsync("JSONRPC", user);
        await SeedAsync(seeder, 1);
        using var http = server.CreateClient(user);

        async Task<JsonObject> ListAsync(string jsonParams, string query)
        {
            using var request = transport == "jsonrpc"
                ? new HttpRequestMessage(HttpMethod.Post, "agents/echo/jsonrpc")
                {
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ListTasks\",\"params\":" + jsonParams + "}", Encoding.UTF8, "application/json"),
                }
                : new HttpRequestMessage(HttpMethod.Get, "agents/echo/rest/tasks" + query);
            request.Headers.Add(A2AConstants.VersionHeader, "1.0");
            using var response = await http.SendAsync(request, Ct);
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
            return Assert.Single((transport == "jsonrpc" ? body["result"]! : body)["tasks"]!.AsArray())!.AsObject();
        }

        var unset = await ListAsync("{}", "");
        var noHistory = await ListAsync("""{"historyLength":0}""", "?historyLength=0");
        var withArtifacts = await ListAsync("""{"includeArtifacts":true}""", "?includeArtifacts=true");
        var falseArtifacts = await ListAsync("""{"includeArtifacts":false}""", "?includeArtifacts=false");

        Assert.True(unset.ContainsKey("history"));
        Assert.False(noHistory.ContainsKey("history"));
        Assert.True(withArtifacts.ContainsKey("artifacts"));
        Assert.False(falseArtifacts.ContainsKey("artifacts"));
    }
}
