using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Tasks;

namespace Ambinder.A2A.Server.Tests.Tasks;

public class InMemoryTaskStoreTests
{
    private const string Agent = "echo";
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Message Msg(string id) => new() { MessageId = id, Role = Role.User };

    private static StoredTask Make(string id, DateTimeOffset? ts, string owner = "alice", string ctx = "c1",
        TaskState state = TaskState.Completed, string agent = Agent, int history = 3, int artifacts = 1)
        => new(agent, owner, null, new AgentTask
        {
            Id = id,
            ContextId = ctx,
            Status = new AgentTaskStatus { State = state, Timestamp = ts },
            History = Enumerable.Range(0, history).Select(i => Msg($"{id}-m{i}")).ToList(),
            Artifacts = Enumerable.Range(0, artifacts).Select(i => new Artifact { ArtifactId = $"a{i}" }).ToList(),
        });

    private static async Task<InMemoryTaskStore> Seed(params StoredTask[] tasks)
    {
        var store = new InMemoryTaskStore();
        foreach (var t in tasks) await store.SaveAsync(t, CancellationToken.None);
        return store;
    }

    private static ValueTask<ListTasksResponse> List(InMemoryTaskStore store, string owner = "alice", string agent = Agent,
        ListTasksRequest? request = null)
        => store.ListAsync(agent, owner, request ?? new ListTasksRequest(), CancellationToken.None);

    private static ValueTask<ListTasksResponse> List(int? pageSize = null, string? pageToken = null)
        => List(new InMemoryTaskStore(), request: new ListTasksRequest { PageSize = pageSize, PageToken = pageToken });

    [Fact]
    public async Task List_is_sorted_by_status_timestamp_descending_and_paged()
    {
        var store = await Seed(Enumerable.Range(0, 120).Select(i => Make($"t{i:000}", T0.AddMinutes(i))).ToArray());

        var p1 = await List(store);
        Assert.Equal(50, p1.Tasks.Count);
        Assert.Equal(50, p1.PageSize);
        Assert.Equal(120, p1.TotalSize);
        Assert.NotEqual("", p1.NextPageToken);
        Assert.Equal("t119", p1.Tasks[0].Id);
        Assert.Equal("t070", p1.Tasks[^1].Id);

        var p2 = await List(store, request: new ListTasksRequest { PageToken = p1.NextPageToken });
        Assert.Equal(50, p2.Tasks.Count);
        Assert.Equal("t069", p2.Tasks[0].Id);
        Assert.NotEqual("", p2.NextPageToken);

        var p3 = await List(store, request: new ListTasksRequest { PageToken = p2.NextPageToken });
        Assert.Equal(20, p3.Tasks.Count);
        Assert.Equal("", p3.NextPageToken);
        Assert.Equal("t000", p3.Tasks[^1].Id);
        Assert.Equal(120, p3.TotalSize);
    }

    [Fact]
    public async Task List_breaks_timestamp_ties_by_id_descending_and_pages_stably()
    {
        var store = await Seed(Make("a", T0), Make("b", T0), Make("c", T0), Make("none", null));
        var p1 = await List(store, request: new ListTasksRequest { PageSize = 2 });
        Assert.Equal(["c", "b"], p1.Tasks.Select(t => t.Id));
        var p2 = await List(store, request: new ListTasksRequest { PageSize = 2, PageToken = p1.NextPageToken });
        Assert.Equal(["a", "none"], p2.Tasks.Select(t => t.Id));
        Assert.Equal("", p2.NextPageToken);
    }

    [Fact]
    public async Task List_filters_by_owner_contextId_status_and_timestamp_after()
    {
        var store = await Seed(
            Make("t1", T0.AddMinutes(1)),
            Make("t2", T0.AddMinutes(2), ctx: "c2"),
            Make("t3", T0.AddMinutes(3), state: TaskState.Working),
            Make("t4", T0.AddMinutes(4), owner: "bob"),
            Make("t5", T0.AddMinutes(5), agent: "other"));

        Assert.Equal(["t3", "t2", "t1"], (await List(store)).Tasks.Select(t => t.Id));
        Assert.Equal(["t4"], (await List(store, owner: "bob")).Tasks.Select(t => t.Id));
        Assert.Equal(["t5"], (await List(store, agent: "other")).Tasks.Select(t => t.Id));
        Assert.Equal(["t2"], (await List(store, request: new ListTasksRequest { ContextId = "c2" })).Tasks.Select(t => t.Id));
        Assert.Equal(["t3"], (await List(store, request: new ListTasksRequest { Status = TaskState.Working })).Tasks.Select(t => t.Id));
        var after = await List(store, request: new ListTasksRequest { StatusTimestampAfter = T0.AddMinutes(2) });
        Assert.Equal(["t3"], after.Tasks.Select(t => t.Id));
        Assert.Equal(1, after.TotalSize);
    }

    [Fact]
    public async Task List_omits_artifacts_unless_includeArtifacts_true()
    {
        var store = await Seed(Make("t1", T0));
        Assert.Empty((await List(store)).Tasks[0].Artifacts);
        Assert.Empty((await List(store, request: new ListTasksRequest { IncludeArtifacts = false })).Tasks[0].Artifacts);
        Assert.Single((await List(store, request: new ListTasksRequest { IncludeArtifacts = true })).Tasks[0].Artifacts);
    }

    [Fact]
    public async Task List_applies_historyLength_zero_as_no_history()
    {
        var store = await Seed(Make("t1", T0));
        Assert.Empty((await List(store, request: new ListTasksRequest { HistoryLength = 0 })).Tasks[0].History);
        Assert.Equal(3, (await List(store)).Tasks[0].History.Count);
        var last2 = (await List(store, request: new ListTasksRequest { HistoryLength = 2 })).Tasks[0].History;
        Assert.Equal(["t1-m1", "t1-m2"], last2.Select(m => m.MessageId));
        Assert.Equal(3, (await List(store, request: new ListTasksRequest { HistoryLength = 10 })).Tasks[0].History.Count);
    }

    [Fact]
    public async Task Negative_historyLength_throws_invalid_params()
    {
        var store = await Seed(Make("t1", T0));
        var ex = await Assert.ThrowsAsync<A2AException>(async () => await List(store, request: new ListTasksRequest { HistoryLength = -1 }));
        Assert.Equal(A2AErrorCode.InvalidParams, ex.Code);
    }

    [Theory, InlineData(0), InlineData(101)]
    public async Task Page_size_out_of_range_throws_invalid_params(int size)
        => Assert.Equal(A2AErrorCode.InvalidParams, (await Assert.ThrowsAsync<A2AException>(async () => await List(pageSize: size))).Code);

    [Theory, InlineData("not-a-token"), InlineData("Z2FyYmFnZQ")]
    public async Task Invalid_page_token_throws_invalid_params(string token)
        => Assert.Equal(A2AErrorCode.InvalidParams, (await Assert.ThrowsAsync<A2AException>(async () => await List(pageToken: token))).Code);

    [Fact]
    public void PageToken_round_trips_ids_containing_colons()
    {
        var token = PageToken.Encode(T0, "id:with:colons");
        Assert.True(PageToken.TryDecode(token, out var ts, out var id));
        Assert.Equal(T0, ts);
        Assert.Equal("id:with:colons", id);
    }

    [Fact]
    public async Task Get_returns_null_for_unknown_and_is_keyed_by_agent()
    {
        var store = await Seed(Make("t1", T0));
        Assert.Null(await store.GetAsync(Agent, "nope", CancellationToken.None));
        Assert.Null(await store.GetAsync("other", "t1", CancellationToken.None));
        var got = await store.GetAsync(Agent, "t1", CancellationToken.None);
        Assert.Equal("alice", got!.Owner);
        Assert.Equal(3, got.Task.History.Count);
    }

    [Fact]
    public async Task Store_does_not_share_mutable_state_with_callers()
    {
        var history = new List<Message> { Msg("m0") };
        var original = new StoredTask(Agent, "alice", null, new AgentTask { Id = "t1", History = history, Status = new AgentTaskStatus { Timestamp = T0 } });
        var store = await Seed(original);

        history.Add(Msg("m1")); // mutate after save
        var read = await store.GetAsync(Agent, "t1", CancellationToken.None);
        Assert.Single(read!.Task.History);

        ((List<Message>)read.Task.History).Add(Msg("m2")); // mutate a read copy
        var again = await store.GetAsync(Agent, "t1", CancellationToken.None);
        Assert.Single(again!.Task.History);
    }
}
