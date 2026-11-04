using System.Globalization;
using System.Text.Json;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Stores;
using Microsoft.Extensions.Time.Testing;

namespace Ambinder.A2A.Testing.Conformance;

/// <summary>Builders for the records the conformance cases write. Ids come from a counter, never from the clock or a GUID, so failures are reproducible.</summary>
internal static class StoreTestData
{
    private static long _counter;

    /// <summary>Returns a new unique, short id with the given prefix.</summary>
    internal static string NextId(string prefix) =>
        prefix + "-" + Interlocked.Increment(ref _counter).ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>True for terminal and interrupted states (the core's <c>TaskStateRules.IsFinal</c>, which is internal to the core).</summary>
    internal static bool IsFinal(TaskState state) =>
        state is TaskState.Completed or TaskState.Failed or TaskState.Canceled or TaskState.Rejected
            or TaskState.InputRequired or TaskState.AuthRequired;

    /// <summary>A text message with a fixed id.</summary>
    internal static Message NewMessage(string id, string text, Role role = Role.User) =>
        new() { MessageId = id, Role = role, Parts = [Part.FromText(text)] };

    /// <summary>An artifact with one text part.</summary>
    internal static Artifact NewArtifact(string id, string text) =>
        new() { ArtifactId = id, Name = "artifact " + id, Parts = [Part.FromText(text)] };

    /// <summary>Parses JSON text into a detached element.</summary>
    internal static JsonElement Json(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// A task ready for <c>CreateAsync</c>: no <c>Version</c>, <c>Status.Timestamp</c> set (the core always sets it).
    /// <paramref name="timestamp"/> defaults to the clock's now; <c>CreatedAt</c> and <c>UpdatedAt</c> default to now.
    /// </summary>
    internal static StoredTask NewTask(
        FakeTimeProvider time,
        string tenant = "t1",
        string agent = "agent",
        string owner = "alice",
        TaskState state = TaskState.Submitted,
        string contextId = "ctx",
        int history = 1,
        int artifacts = 0,
        DateTimeOffset? timestamp = null,
        string? taskId = null,
        string? originMessageId = null,
        DateTimeOffset? expiresAt = null,
        TaskRunner? runner = null,
        DateTimeOffset? cancelRequestedAt = null,
        DateTimeOffset? updatedAt = null)
    {
        var now = time.GetUtcNow();
        var id = taskId ?? NextId("task");
        var messages = new List<Message>(history);
        for (var i = 1; i <= history; i++)
            messages.Add(NewMessage($"{id}-m{i}", $"message {i}", i % 2 == 1 ? Role.User : Role.Agent));
        var arts = new List<Artifact>(artifacts);
        for (var i = 1; i <= artifacts; i++)
            arts.Add(NewArtifact($"{id}-a{i}", $"artifact text {i}"));

        return new StoredTask
        {
            Key = new TaskKey(tenant, agent, id),
            Owner = owner,
            Task = new AgentTask
            {
                Id = id,
                ContextId = contextId,
                Status = new AgentTaskStatus { State = state, Timestamp = timestamp ?? now },
                History = messages,
                Artifacts = arts,
            },
            CreatedAt = now,
            UpdatedAt = updatedAt ?? now,
            ExpiresAt = expiresAt,
            Runner = runner,
            CancelRequestedAt = cancelRequestedAt,
            OriginMessageId = originMessageId,
        };
    }

    /// <summary>A runner record claimed at <paramref name="claimedAt"/>.</summary>
    internal static TaskRunner NewRunner(string instanceId, DateTimeOffset claimedAt) =>
        new() { InstanceId = instanceId, ClaimedAt = claimedAt };

    /// <summary>An outbox row for <paramref name="task"/>, due now.</summary>
    internal static OutboxEntry NewOutbox(StoredTask task, FakeTimeProvider time, long sequence, string lane = "cfg-1", string kind = "push")
    {
        var now = time.GetUtcNow();
        return new OutboxEntry
        {
            Id = NextId("outbox"),
            Task = task.Key,
            Sequence = sequence,
            Kind = kind,
            Lane = lane,
            Payload = new byte[] { 1, 2, 3, (byte)(sequence & 0x7f) },
            CreatedAt = now,
            NextAttemptAt = now,
        };
    }

    /// <summary>A push config with a fixed id and URL.</summary>
    internal static TaskPushNotificationConfig NewPushConfig(string id, string taskId, string url = "https://example.test/hook", string? token = null) =>
        new() { Id = id, TaskId = taskId, Url = url, Token = token };

    /// <summary>Creates <paramref name="task"/> and fails the case unless the store reports <c>Created</c>.</summary>
    internal static async Task<StoredTask> CreateAsync(StoreSet s, StoredTask task, CancellationToken ct, params OutboxEntry[] outbox)
    {
        var result = await s.Tasks.CreateAsync(new TaskWrite { Task = task, Outbox = outbox }, ct).ConfigureAwait(false);
        ConformanceAssert.True(result.Created, $"CreateAsync must return Created = true for new task '{task.Key.TaskId}'.");
        ConformanceAssert.NotNull(result.Task.Version, "CreateAsync must return a Version.");
        return result.Task;
    }

    /// <summary>Updates <paramref name="current"/> with <paramref name="next"/> (same key), using <paramref name="current"/>'s version.</summary>
    internal static async Task<StoredTask> UpdateAsync(StoreSet s, StoredTask current, StoredTask next, CancellationToken ct, params OutboxEntry[] outbox) =>
        await s.Tasks.UpdateAsync(new TaskWrite { Task = next, Outbox = outbox }, current.Version!, ct).ConfigureAwait(false);

    /// <summary>The task with a new state and status timestamp.</summary>
    internal static StoredTask WithState(StoredTask task, TaskState state, DateTimeOffset timestamp) =>
        task with { Task = task.Task with { Status = task.Task.Status with { State = state, Timestamp = timestamp } } };
}
