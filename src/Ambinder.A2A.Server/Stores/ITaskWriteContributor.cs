using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Stores;

/// <summary>A seam that lets another package (push, package 08) add outbox rows to a task write without the core knowing about it.</summary>
public interface ITaskWriteContributor
{
    /// <summary>Called by the core for each committed event, before the store write. May add outbox rows.</summary>
    /// <param name="context">The write in progress.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>A task that completes when the call is done.</returns>
    ValueTask ContributeAsync(TaskWriteContext context, CancellationToken ct);
}

/// <summary>One task write in progress. Created by the core.</summary>
public sealed class TaskWriteContext
{
    private readonly List<OutboxEntry> _outbox = [];

    internal TaskWriteContext(StoredTask? previous, StoredTask next, StreamResponse evt)
        => (Previous, Next, Event) = (previous, next, evt);

    /// <summary>The task before this event; null when this event creates the task.</summary>
    public StoredTask? Previous { get; }

    /// <summary>The task after this event, as it will be written (Sequence already incremented).</summary>
    public StoredTask Next { get; }

    /// <summary>The event being committed.</summary>
    public StreamResponse Event { get; }

    /// <summary>Rows added so far.</summary>
    public IReadOnlyList<OutboxEntry> Outbox => _outbox;

    /// <summary>Adds an outbox row to this write. Its <c>Task</c> must equal <c>Next.Key</c> and its <c>Sequence</c> must equal <c>Next.Sequence</c>.</summary>
    /// <param name="entry">The row to add.</param>
    public void AddOutbox(OutboxEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Task != Next.Key || entry.Sequence != Next.Sequence)
            throw new ArgumentException("Outbox entry must belong to this task write (same Task key and Sequence).", nameof(entry));
        _outbox.Add(entry);
    }
}
