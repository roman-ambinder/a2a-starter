using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Tasks;

namespace Ambinder.A2A.Server.Tests.Tasks;

public class TaskEventBrokerTests
{
    private const string Agent = "a";
    private const string TaskId = "t1";

    private static StreamResponse Status(TaskState state) => new()
    {
        StatusUpdate = new TaskStatusUpdateEvent { TaskId = TaskId, ContextId = "c", Status = new AgentTaskStatus { State = state } },
    };

    private static StreamResponse Artifact(string id) => new()
    {
        ArtifactUpdate = new TaskArtifactUpdateEvent { TaskId = TaskId, ContextId = "c", Artifact = new Artifact { ArtifactId = id } },
    };

    [Fact]
    public async Task Subscriber_receives_events_in_order_and_completes_after_terminal()
    {
        var broker = new TaskEventBroker();
        var stream = broker.Subscribe(Agent, TaskId, CancellationToken.None);

        broker.Publish(Agent, TaskId, Status(TaskState.Working));
        broker.Publish(Agent, TaskId, Artifact("x"));
        broker.Publish(Agent, TaskId, Status(TaskState.Completed));
        broker.Publish(Agent, TaskId, Status(TaskState.Working)); // after terminal: nobody listening

        var events = await TestHost.CollectAsync(stream);
        Assert.Equal(3, events.Count);
        Assert.Equal(TaskState.Working, events[0].StatusUpdate!.Status.State);
        Assert.Equal("x", events[1].ArtifactUpdate!.Artifact.ArtifactId);
        Assert.Equal(TaskState.Completed, events[2].StatusUpdate!.Status.State);
        Assert.Equal(0, broker.SubscriberCount(Agent, TaskId));
    }

    [Theory]
    [InlineData(TaskState.InputRequired)]
    [InlineData(TaskState.AuthRequired)]
    [InlineData(TaskState.Failed)]
    public async Task Stream_completes_after_interrupted_or_terminal_state(TaskState state)
    {
        var broker = new TaskEventBroker();
        var stream = broker.Subscribe(Agent, TaskId, CancellationToken.None);

        broker.Publish(Agent, TaskId, Status(state));

        Assert.Single(await TestHost.CollectAsync(stream));
    }

    [Fact]
    public async Task Until_terminal_subscription_outlives_an_interrupted_state()
    {
        var broker = new TaskEventBroker();
        var sendStream = broker.Subscribe(Agent, TaskId, CancellationToken.None);
        using var subscription = broker.Open(Agent, TaskId, CancellationToken.None, untilTerminal: true);

        broker.Publish(Agent, TaskId, Status(TaskState.InputRequired));
        Assert.Single(await TestHost.CollectAsync(sendStream));
        Assert.Equal(1, broker.SubscriberCount(Agent, TaskId));

        broker.Publish(Agent, TaskId, Status(TaskState.Working));
        broker.Publish(Agent, TaskId, Status(TaskState.Completed));

        var events = await TestHost.CollectAsync(subscription.ReadAllAsync(CancellationToken.None));
        Assert.Equal([TaskState.InputRequired, TaskState.Working, TaskState.Completed], events.Select(e => e.StatusUpdate!.Status.State));
        Assert.Equal(0, broker.SubscriberCount(Agent, TaskId));
    }

    [Fact]
    public async Task Stream_completes_after_a_message()
    {
        var broker = new TaskEventBroker();
        var stream = broker.Subscribe(Agent, TaskId, CancellationToken.None);

        broker.Publish(Agent, TaskId, new StreamResponse { Message = TestHost.AgentText("hi") });

        Assert.NotNull(Assert.Single(await TestHost.CollectAsync(stream)).Message);
    }

    [Fact]
    public async Task Events_for_other_tasks_and_agents_are_not_delivered()
    {
        var broker = new TaskEventBroker();
        var stream = broker.Subscribe(Agent, TaskId, CancellationToken.None);

        broker.Publish(Agent, "other", Status(TaskState.Working));
        broker.Publish("other-agent", TaskId, Status(TaskState.Working));
        broker.Publish(Agent, TaskId, Status(TaskState.Completed));

        Assert.Single(await TestHost.CollectAsync(stream));
    }

    [Fact]
    public void Publish_without_subscribers_does_nothing()
    {
        var broker = new TaskEventBroker();

        broker.Publish(Agent, TaskId, Status(TaskState.Working));

        Assert.Equal(0, broker.SubscriberCount(Agent, TaskId));
    }

    [Fact]
    public async Task Cancelling_one_subscriber_keeps_the_others()
    {
        var broker = new TaskEventBroker();
        using var cts = new CancellationTokenSource();
        var cancelled = broker.Subscribe(Agent, TaskId, cts.Token);
        var kept = broker.Subscribe(Agent, TaskId, CancellationToken.None);
        Assert.Equal(2, broker.SubscriberCount(Agent, TaskId));

        await cts.CancelAsync();
        Assert.Equal(1, broker.SubscriberCount(Agent, TaskId));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestHost.CollectAsync(cancelled));

        broker.Publish(Agent, TaskId, Status(TaskState.Completed));
        Assert.Single(await TestHost.CollectAsync(kept));
    }

    [Fact]
    public async Task Disposing_an_enumeration_removes_the_subscriber()
    {
        var broker = new TaskEventBroker();
        var stream = broker.Subscribe(Agent, TaskId, CancellationToken.None);
        broker.Publish(Agent, TaskId, Status(TaskState.Working));

        await foreach (var _ in stream) break;

        Assert.Equal(0, broker.SubscriberCount(Agent, TaskId));
    }

    [Fact]
    public async Task Slow_subscriber_is_dropped_without_blocking_the_publisher()
    {
        var broker = new TaskEventBroker(capacity: 2);
        var slow = broker.Subscribe(Agent, TaskId, CancellationToken.None);

        for (var i = 0; i < 5; i++) broker.Publish(Agent, TaskId, Artifact($"a{i}")); // returns: never waits

        Assert.Equal(0, broker.SubscriberCount(Agent, TaskId));
        var received = new List<StreamResponse>();
        var ex = await Assert.ThrowsAsync<A2AException>(async () =>
        {
            await foreach (var e in slow) received.Add(e);
        });
        Assert.Equal(A2AErrorCode.InternalError, ex.Code);
        Assert.Equal(["a0", "a1"], received.Select(e => e.ArtifactUpdate!.Artifact.ArtifactId));
    }
}
