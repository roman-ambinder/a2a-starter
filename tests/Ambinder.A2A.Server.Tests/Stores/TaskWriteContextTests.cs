using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Stores;
using Xunit;

namespace Ambinder.A2A.Server.Tests.Stores;

public class TaskWriteContextTests
{
    private static readonly TaskKey Key = new("", "agent", "t1");

    private static TaskWriteContext NewContext()
    {
        var next = new StoredTask { Key = Key, Owner = "o", Task = new AgentTask { Id = "t1" }, Sequence = 3 };
        return new TaskWriteContext(null, next, new StreamResponse());
    }

    private static OutboxEntry Entry(TaskKey key, long sequence) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Task = key,
        Sequence = sequence,
        Kind = "push",
        Lane = "cfg",
        Payload = ReadOnlyMemory<byte>.Empty,
    };

    [Fact]
    public void AddOutbox_accepts_a_matching_entry()
    {
        var ctx = NewContext();
        var entry = Entry(Key, 3);

        ctx.AddOutbox(entry);

        Assert.Same(entry, Assert.Single(ctx.Outbox));
    }

    [Fact]
    public void AddOutbox_rejects_another_task()
    {
        var ctx = NewContext();

        Assert.Throws<ArgumentException>(() => ctx.AddOutbox(Entry(Key with { TaskId = "other" }, 3)));
        Assert.Empty(ctx.Outbox);
    }

    [Fact]
    public void AddOutbox_rejects_another_sequence()
    {
        var ctx = NewContext();

        Assert.Throws<ArgumentException>(() => ctx.AddOutbox(Entry(Key, 4)));
        Assert.Empty(ctx.Outbox);
    }

    [Fact]
    public void AddOutbox_rejects_null()
        => Assert.Throws<ArgumentNullException>(() => NewContext().AddOutbox(null!));
}
