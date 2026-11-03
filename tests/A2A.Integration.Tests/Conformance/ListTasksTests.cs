using System.Text;
using System.Text.Json.Nodes;
using A2A.Client;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;

namespace A2A.Integration.Tests.Conformance;

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
}
