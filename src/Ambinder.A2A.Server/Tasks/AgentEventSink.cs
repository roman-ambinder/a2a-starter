using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Tasks;

/// <summary>
/// The <see cref="IAgentEventSink"/> for one execution (or one cancel call). Every event runs under the
/// task's lock: check the rules, apply, save, publish. For a new message the task is created on the
/// agent's first non-reply event, so a direct reply stores nothing.
/// </summary>
internal sealed class AgentEventSink : IAgentEventSink
{
    private const string InvalidResponseText = "Invalid agent response";

    private readonly TaskWriter _writer;
    private readonly TaskExecution? _execution;
    private readonly string _agent;
    private readonly string _taskId;
    private readonly string _contextId;
    private readonly string _owner;
    private readonly string? _tenant;
    private readonly Message? _newTaskMessage; // set → task is created lazily from this user message

    private bool _taskExists;
    private int _events;
    private volatile bool _replied;
    private volatile bool _done;

    private AgentEventSink(TaskWriter writer, TaskExecution? execution, string agent, string taskId, string contextId,
        string owner, string? tenant, Message? newTaskMessage, bool taskExists)
    {
        _writer = writer;
        _execution = execution;
        _agent = agent;
        _taskId = taskId;
        _contextId = contextId;
        _owner = owner;
        _tenant = tenant;
        _newTaskMessage = newTaskMessage;
        _taskExists = taskExists;
    }

    /// <summary>For a new message; <paramref name="userMessage"/> already carries the task and context ids.</summary>
    public static AgentEventSink ForNewTask(TaskWriter writer, TaskExecution execution, string owner, string? tenant, Message userMessage)
        => new(writer, execution, execution.AgentName, execution.TaskId, userMessage.ContextId!, owner, tenant, userMessage, taskExists: false);

    /// <summary>For a continuation (with its execution) or a cancel call (no execution).</summary>
    public static AgentEventSink ForExistingTask(TaskWriter writer, TaskExecution? execution, StoredTask stored)
        => new(writer, execution, stored.AgentName, stored.Task.Id, stored.Task.ContextId ?? "", stored.Owner, stored.Tenant, null, taskExists: true);

    /// <summary>True once the agent replied or this execution reached a final state.</summary>
    public bool Done => _replied || _done;

    public async ValueTask ReplyAsync(Message message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        using (await _writer.LockAsync(_agent, _taskId, ct).ConfigureAwait(false))
        {
            ThrowIfFinished();
            if (_newTaskMessage is null || _taskExists || _events > 0 || _execution is null)
            {
                await FailLockedAsync(InvalidResponseText, create: false).ConfigureAwait(false);
                throw Invalid("ReplyAsync is only valid as the agent's first and only event on a new message.");
            }

            var reply = message with
            {
                MessageId = string.IsNullOrEmpty(message.MessageId) ? Guid.NewGuid().ToString() : message.MessageId,
                ContextId = string.IsNullOrEmpty(message.ContextId) ? _contextId : message.ContextId,
                Role = message.Role == Role.Unspecified ? Role.Agent : message.Role,
            };
            _replied = true;
            _execution.Reply = reply;
            _writer.Broker.Publish(_agent, _taskId, new StreamResponse { Message = reply });
            _execution.FirstResult.TrySetResult(new SendMessageResponse { Message = reply });
            _execution.Settled.TrySetResult();
        }
    }

    public async ValueTask UpdateStatusAsync(TaskState state, Message? message, CancellationToken ct)
    {
        using (await _writer.LockAsync(_agent, _taskId, ct).ConfigureAwait(false))
        {
            var stored = await BeginEventLockedAsync().ConfigureAwait(false);
            var from = stored.Task.Status.State;
            if (!TaskStateRules.CanAgentTransition(from, state))
            {
                await FailLockedAsync(InvalidResponseText, create: false).ConfigureAwait(false);
                throw Invalid($"Illegal task state transition {from} -> {state}.");
            }
            stored = await _writer.CommitStatusAsync(stored, state, message is null ? null : Normalize(message)).ConfigureAwait(false);
            AfterEventLocked(stored);
        }
    }

    public async ValueTask AddArtifactAsync(Artifact artifact, bool append, bool lastChunk, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        using (await _writer.LockAsync(_agent, _taskId, ct).ConfigureAwait(false))
        {
            var stored = await BeginEventLockedAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(artifact.ArtifactId) || artifact.Parts.Count == 0 || artifact.Parts.Any(p => !p.IsValid))
            {
                await FailLockedAsync(InvalidResponseText, create: false).ConfigureAwait(false);
                throw Invalid("An artifact needs an artifactId and at least one valid part.");
            }

            var artifacts = stored.Task.Artifacts.ToList();
            var index = artifacts.FindIndex(a => a.ArtifactId == artifact.ArtifactId);
            if (index < 0) artifacts.Add(artifact);
            else if (append) artifacts[index] = artifacts[index] with { Parts = [.. artifacts[index].Parts, .. artifact.Parts] };
            else artifacts[index] = artifact;

            stored = stored with { Task = stored.Task with { Artifacts = artifacts } };
            await _writer.CommitAsync(stored, new StreamResponse
            {
                ArtifactUpdate = new TaskArtifactUpdateEvent
                {
                    TaskId = _taskId,
                    ContextId = _contextId,
                    Artifact = artifact,
                    Append = append,
                    LastChunk = lastChunk,
                },
            }).ConfigureAwait(false);
            AfterEventLocked(stored);
        }
    }

    /// <summary>
    /// Fails the task with a server-authored agent message (creating it first for a new message), unless
    /// the agent replied or the task is already terminal. Used when the agent throws or ends without a final state.
    /// </summary>
    public async ValueTask FailAsync(string text)
    {
        using (await _writer.LockAsync(_agent, _taskId, CancellationToken.None).ConfigureAwait(false))
            await FailLockedAsync(text, create: true).ConfigureAwait(false);
    }

    private void ThrowIfFinished()
    {
        if (_replied) throw Invalid("No events are allowed after ReplyAsync.");
        if (_done) throw Invalid("This execution already reached a terminal or interrupted state.");
    }

    private async ValueTask<StoredTask> BeginEventLockedAsync()
    {
        ThrowIfFinished();
        var stored = await LoadOrCreateLockedAsync().ConfigureAwait(false);
        if (TaskStateRules.IsTerminal(stored.Task.Status.State))
        {
            // Typically cancelled concurrently. Nothing can change a terminal task.
            _done = true;
            throw Invalid($"Task is already {stored.Task.Status.State}.");
        }
        return stored;
    }

    private void AfterEventLocked(StoredTask stored)
    {
        _events++;
        if (TaskStateRules.IsFinal(stored.Task.Status.State))
        {
            _done = true;
            if (_execution is not null) _execution.FinalTask ??= stored.Task;
            _execution?.Settled.TrySetResult();
        }
        _execution?.FirstResult.TrySetResult(new SendMessageResponse { Task = stored.Task });
    }

    private async ValueTask FailLockedAsync(string text, bool create)
    {
        if (_replied) return;
        StoredTask? stored = _taskExists || create ? await LoadOrCreateLockedAsync().ConfigureAwait(false) : null;
        if (stored is null || TaskStateRules.IsTerminal(stored.Task.Status.State))
        {
            if (stored is not null) _done = true;
            return;
        }
        stored = await _writer.CommitStatusAsync(stored, TaskState.Failed, TaskWriter.AgentMessage(text, _taskId, _contextId)).ConfigureAwait(false);
        AfterEventLocked(stored);
    }

    private async ValueTask<StoredTask> LoadOrCreateLockedAsync()
    {
        if (_taskExists)
        {
            return await _writer.Store.GetAsync(_agent, _taskId, CancellationToken.None).ConfigureAwait(false)
                   ?? throw new InvalidOperationException($"Task '{_taskId}' disappeared from the store.");
        }

        var created = new StoredTask(_agent, _owner, _tenant, new AgentTask
        {
            Id = _taskId,
            ContextId = _contextId,
            Status = new AgentTaskStatus { State = TaskState.Submitted, Timestamp = _writer.Now },
            History = [_newTaskMessage!],
        });
        await _writer.CommitAsync(created, new StreamResponse { Task = created.Task }).ConfigureAwait(false);
        _taskExists = true;
        return created;
    }

    private Message Normalize(Message message) => message with
    {
        MessageId = string.IsNullOrEmpty(message.MessageId) ? Guid.NewGuid().ToString() : message.MessageId,
        TaskId = _taskId,
        ContextId = _contextId,
        Role = message.Role == Role.Unspecified ? Role.Agent : message.Role,
    };

    private static A2AException Invalid(string message) => new(A2AErrorCode.InvalidAgentResponse, message);
}
