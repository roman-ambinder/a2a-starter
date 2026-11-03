using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using A2A.Protocol;
using A2A.Server.Agents;
using A2A.Server.Auth;
using A2A.Server.Push;
using A2A.Server.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A2A.Server.Tasks;

/// <summary>
/// The transport-independent A2A request handler: task lifecycle, agent execution and streaming.
/// Agents run on a background task with their own <see cref="CancellationTokenSource"/>; request tokens
/// only stop the caller's wait. A per-task lock guards every check-and-change; it is never held while
/// the agent runs, only while one event is applied.
/// </summary>
public sealed class TaskManager : IA2ARequestHandler
{
    private readonly AgentCatalog _catalog;
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    private readonly TaskWriter _writer;
    private readonly PushConfigService _push;
    private readonly IOwnerResolver _owners;
    private readonly ConcurrentDictionary<(string Agent, string TaskId), TaskExecution> _running = new();

    // Tasks with a CancelTask in progress. Set and checked under the task lock; continuations are rejected
    // while present, so no new execution can start between the cancel's state check and its CANCELED commit.
    private readonly ConcurrentDictionary<(string Agent, string TaskId), byte> _cancelling = new();

    /// <param name="observers">Called for every task event (seam for push delivery). See <see cref="ITaskEventObserver"/>.</param>
    /// <param name="pushConfigs">Where push configs live. Default: a new in-memory store (share it with the sender).</param>
    /// <param name="pushOptions">Webhook URL rules. Default <see cref="PushOptions"/>.</param>
    /// <param name="time">Clock for status timestamps. Default <see cref="TimeProvider.System"/>.</param>
    /// <param name="owners">Ownership rules; only <see cref="IOwnerResolver.CanContinueInterrupted"/> is used here (the transports
    /// compute <see cref="A2ACallContext.Owner"/>). Default <see cref="DefaultOwnerResolver.Instance"/>.</param>
    public TaskManager(AgentCatalog catalog, ITaskStore store, TaskEventBroker broker, IServiceProvider services,
        IEnumerable<ITaskEventObserver>? observers = null, ILogger<TaskManager>? logger = null, TimeProvider? time = null,
        IPushConfigStore? pushConfigs = null, PushOptions? pushOptions = null, IOwnerResolver? owners = null)
    {
        _owners = owners ?? DefaultOwnerResolver.Instance;
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _writer = new TaskWriter(
            store ?? throw new ArgumentNullException(nameof(store)),
            broker ?? throw new ArgumentNullException(nameof(broker)),
            observers?.ToList() ?? [],
            time ?? TimeProvider.System,
            _logger);
        _push = new PushConfigService(pushConfigs ?? new InMemoryPushConfigStore(), pushOptions ?? new PushOptions());
    }

    public async Task<SendMessageResponse> SendMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct)
    {
        var (execution, _) = await StartAsync(call, request, subscribe: false, ct).ConfigureAwait(false);
        var historyLength = request.Configuration?.HistoryLength;

        if (request.Configuration?.ReturnImmediately == true)
        {
            var first = await execution.FirstResult.Task.WaitAsync(ct).ConfigureAwait(false);
            return first.Task is { } t ? new SendMessageResponse { Task = RequestValidator.ShapeHistory(t, historyLength) } : first;
        }

        await execution.Settled.Task.WaitAsync(ct).ConfigureAwait(false);
        if (execution.Reply is { } reply) return new SendMessageResponse { Message = reply };
        if (execution.FinalTask is { } final) return new SendMessageResponse { Task = RequestValidator.ShapeHistory(final, historyLength) };
        var stored = await _writer.Store.GetAsync(call.AgentName, execution.TaskId, ct).ConfigureAwait(false)
                     ?? throw new A2AException(A2AErrorCode.InternalError, "The agent produced no result.");
        return new SendMessageResponse { Task = RequestValidator.ShapeHistory(stored.Task, historyLength) };
    }

    public async IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(A2ACallContext call, SendMessageRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        RequireStreaming(GetAgent(call));
        var (_, subscription) = await StartAsync(call, request, subscribe: true, ct).ConfigureAwait(false);
        var historyLength = request.Configuration?.HistoryLength;
        using (subscription!)
        {
            await foreach (var evt in subscription!.ReadAllAsync(ct).ConfigureAwait(false))
                yield return evt.Task is { } t ? new StreamResponse { Task = RequestValidator.ShapeHistory(t, historyLength) } : evt;
        }
    }

    public async Task<AgentTask> GetTaskAsync(A2ACallContext call, GetTaskRequest request, CancellationToken ct)
    {
        GetAgent(call);
        RequestValidator.ValidateHistoryLength(request.HistoryLength);
        var stored = await LoadOwnedAsync(call, request.Id, ct).ConfigureAwait(false);
        return RequestValidator.ShapeHistory(stored.Task, request.HistoryLength);
    }

    public async Task<ListTasksResponse> ListTasksAsync(A2ACallContext call, ListTasksRequest request, CancellationToken ct)
    {
        GetAgent(call);
        return await _writer.Store.ListAsync(call.AgentName, call.Owner, request, ct).ConfigureAwait(false);
    }

    public async Task<AgentTask> CancelTaskAsync(A2ACallContext call, CancelTaskRequest request, CancellationToken ct)
    {
        var registration = GetAgent(call);
        StoredTask stored;
        TaskExecution? running;
        var key = (call.AgentName, request.Id);
        using (await _writer.LockAsync(call.AgentName, request.Id, ct).ConfigureAwait(false))
        {
            stored = await LoadOwnedAsync(call, request.Id, ct).ConfigureAwait(false);
            if (TaskStateRules.IsTerminal(stored.Task.Status.State))
                throw new A2AException(A2AErrorCode.TaskNotCancelable, $"Task '{request.Id}' is {stored.Task.Status.State} and cannot be canceled.");
            if (!_cancelling.TryAdd(key, 0))
                throw new A2AException(A2AErrorCode.TaskNotCancelable, $"Task '{request.Id}' is already being canceled.");
            if (_running.TryGetValue(key, out running)) running.CancelRequested = true;
        }

        try
        {
            if (running is not null)
            {
                try { await running.Cts.CancelAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { } // the run already finished and disposed its CTS
            }

            var task = stored.Task;
            var context = new AgentRequestContext(call.AgentName, task.Id, task.ContextId ?? "",
                task.History.LastOrDefault(m => m.Role == Role.User) ?? new Message(), task, call.User,
                RequestValidator.ActiveExtensions(registration.PublicCard, call.RequestedExtensions), null);
            var sink = AgentEventSink.ForExistingTask(_writer, null, stored);
            try
            {
                await InvokeHandlerAsync(registration, h => h.CancelAsync(context, sink, ct)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agent {Agent} CancelAsync failed for task {TaskId}; canceling anyway", call.AgentName, task.Id);
            }

            // Whatever the agent did, a task that is not terminal now ends CANCELED.
            using (await _writer.LockAsync(call.AgentName, request.Id, CancellationToken.None).ConfigureAwait(false))
            {
                stored = await _writer.Store.GetAsync(call.AgentName, request.Id, CancellationToken.None).ConfigureAwait(false) ?? stored;
                if (!TaskStateRules.IsTerminal(stored.Task.Status.State))
                    stored = await _writer.CommitStatusAsync(stored, TaskState.Canceled, null).ConfigureAwait(false);
                if (running is not null) running.FinalTask ??= stored.Task;
                _cancelling.TryRemove(key, out _);
            }
        }
        finally
        {
            _cancelling.TryRemove(key, out _);
            running?.Settled.TrySetResult();
        }

        if (stored.Task.Status.State != TaskState.Canceled)
            throw new A2AException(A2AErrorCode.TaskNotCancelable, $"Task '{request.Id}' ended as {stored.Task.Status.State} before it could be canceled.");
        return stored.Task;
    }

    public async IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(A2ACallContext call, SubscribeToTaskRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        RequireStreaming(GetAgent(call));
        var (snapshot, subscription) = await OpenSubscriptionAsync(call, request.Id, ct).ConfigureAwait(false);
        using (subscription)
        {
            yield return new StreamResponse { Task = snapshot };
            await foreach (var evt in subscription.ReadAllAsync(ct).ConfigureAwait(false))
                yield return evt;
        }
    }

    public async Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(A2ACallContext call, TaskPushNotificationConfig request, CancellationToken ct)
    {
        RequirePush(GetAgent(call));
        await LoadOwnedAsync(call, request.TaskId ?? "", ct).ConfigureAwait(false);
        return await _push.SaveAsync(call.AgentName, request.TaskId!, request, ct).ConfigureAwait(false);
    }

    public async Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(A2ACallContext call, GetTaskPushNotificationConfigRequest request, CancellationToken ct)
    {
        RequirePush(GetAgent(call));
        await LoadOwnedAsync(call, request.TaskId, ct).ConfigureAwait(false);
        return await _push.GetAsync(call.AgentName, request.TaskId, request.Id, ct).ConfigureAwait(false)
               ?? throw new A2AException(A2AErrorCode.TaskNotFound, $"Push notification config '{request.Id}' was not found for task '{request.TaskId}'.");
    }

    public async Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(A2ACallContext call, ListTaskPushNotificationConfigsRequest request, CancellationToken ct)
    {
        RequirePush(GetAgent(call));
        await LoadOwnedAsync(call, request.TaskId, ct).ConfigureAwait(false);
        return await _push.ListAsync(call.AgentName, request.TaskId, request.PageSize, request.PageToken, ct).ConfigureAwait(false);
    }

    public async Task DeleteTaskPushNotificationConfigAsync(A2ACallContext call, DeleteTaskPushNotificationConfigRequest request, CancellationToken ct)
    {
        RequirePush(GetAgent(call));
        await LoadOwnedAsync(call, request.TaskId, ct).ConfigureAwait(false);
        await _push.DeleteAsync(call.AgentName, request.TaskId, request.Id, ct).ConfigureAwait(false); // idempotent
    }

    public Task<AgentCard> GetExtendedAgentCardAsync(A2ACallContext call, GetExtendedAgentCardRequest request, CancellationToken ct)
    {
        var registration = GetAgent(call);
        // Spec 3.3.4: capability not declared → UnsupportedOperation; declared but no card → ExtendedAgentCardNotConfigured.
        if (registration.PublicCard.Capabilities.ExtendedAgentCard != true)
            throw new A2AException(A2AErrorCode.UnsupportedOperation,
                "This agent does not offer an extended agent card (capabilities.extendedAgentCard is not true). Use the public card.");
        if (registration.ExtendedCard is null)
            throw new A2AException(A2AErrorCode.ExtendedAgentCardNotConfigured,
                "This agent declares an extended agent card but none is configured. Use the public card.");
        return Task.FromResult(registration.ExtendedCard);
    }

    /// <summary>Validates the message, creates or resumes the task and starts the agent in the background.</summary>
    private async Task<(TaskExecution Execution, TaskEventBroker.Subscription? Subscription)> StartAsync(
        A2ACallContext call, SendMessageRequest request, bool subscribe, CancellationToken ct)
    {
        var registration = GetAgent(call);
        RequestValidator.ValidateMessage(request, registration.PublicCard);
        var extensions = RequestValidator.ResolveExtensions(registration.PublicCard, call.RequestedExtensions);
        var pushConfig = request.Configuration?.TaskPushNotificationConfig;
        if (pushConfig is not null)
        {
            RequirePush(registration);
            await _push.ValidateAsync(pushConfig, ct).ConfigureAwait(false);
        }

        var message = request.Message;
        var agent = call.AgentName;
        TaskExecution execution;
        TaskEventBroker.Subscription? subscription = null;
        AgentRequestContext context;
        AgentEventSink sink;

        if (string.IsNullOrEmpty(message.TaskId))
        {
            var taskId = Guid.NewGuid().ToString();
            var contextId = string.IsNullOrEmpty(message.ContextId) ? Guid.NewGuid().ToString() : message.ContextId;
            var userMessage = message with { TaskId = taskId, ContextId = contextId };
            // Saved before the agent starts, so the first event is already delivered to it.
            if (pushConfig is not null) await _push.SaveAsync(agent, taskId, pushConfig, ct).ConfigureAwait(false);
            execution = new TaskExecution(agent, taskId);
            _running[(agent, taskId)] = execution;
            if (subscribe) subscription = _writer.Broker.Open(agent, taskId, ct);
            sink = AgentEventSink.ForNewTask(_writer, execution, call.Owner, call.Tenant, userMessage);
            context = new AgentRequestContext(agent, taskId, contextId, userMessage, null, call.User, extensions, request.Configuration);
        }
        else
        {
            var taskId = message.TaskId;
            using (await _writer.LockAsync(agent, taskId, ct).ConfigureAwait(false))
            {
                var stored = await LoadForContinuationAsync(call, taskId, ct).ConfigureAwait(false);
                var state = stored.Task.Status.State;
                if (TaskStateRules.IsTerminal(state))
                    throw new A2AException(A2AErrorCode.UnsupportedOperation, $"Task '{taskId}' is {state}; a terminal task cannot be continued.");
                if (!TaskStateRules.IsInterrupted(state))
                    throw new A2AException(A2AErrorCode.UnsupportedOperation, $"Task '{taskId}' is {state}; only a task waiting for input can be continued.");
                if (_cancelling.ContainsKey((agent, taskId)))
                    throw new A2AException(A2AErrorCode.UnsupportedOperation, $"Task '{taskId}' is being canceled.");
                if (!string.IsNullOrEmpty(message.ContextId) && message.ContextId != stored.Task.ContextId)
                    throw new A2AException(A2AErrorCode.InvalidParams, "message.contextId does not match the task's contextId.");

                var userMessage = message with { TaskId = taskId, ContextId = stored.Task.ContextId };
                stored = stored with
                {
                    Task = stored.Task with
                    {
                        Status = new AgentTaskStatus { State = TaskState.Submitted, Timestamp = _writer.Now },
                        History = [.. stored.Task.History, userMessage],
                    },
                };
                execution = new TaskExecution(agent, taskId);
                if (pushConfig is not null) await _push.SaveAsync(agent, taskId, pushConfig, ct).ConfigureAwait(false);
                if (subscribe) subscription = _writer.Broker.Open(agent, taskId, ct);
                try
                {
                    await _writer.CommitAsync(stored, new StreamResponse { Task = stored.Task }).ConfigureAwait(false);
                }
                catch
                {
                    subscription?.Dispose();
                    execution.Cts.Dispose();
                    throw;
                }
                _running[(agent, taskId)] = execution;
                sink = AgentEventSink.ForExistingTask(_writer, execution, stored);
                context = new AgentRequestContext(agent, taskId, stored.Task.ContextId ?? "", userMessage, stored.Task, call.User,
                    extensions, request.Configuration);
            }
        }

        _ = Task.Run(() => RunAsync(registration, execution, context, sink), CancellationToken.None);
        return (execution, subscription);
    }

    /// <summary>Runs the agent and makes sure the task ends in a final state whatever the agent does.</summary>
    private async Task RunAsync(AgentRegistration registration, TaskExecution execution, AgentRequestContext context, AgentEventSink sink)
    {
        var ct = execution.Cts.Token;
        // The parent is Activity.Current, which flows through Task.Run from the operation span.
        using var span = ServerInstruments.Source.StartActivity("a2a.agent.run");
        span?.SetTag(A2ATelemetryNames.Agent, execution.AgentName);
        span?.SetTag(A2ATelemetryNames.TaskId, execution.TaskId);
        if (!string.IsNullOrEmpty(context.ContextId)) span?.SetTag(A2ATelemetryNames.ContextId, context.ContextId);
        try
        {
            await InvokeHandlerAsync(registration, h => h.ExecuteAsync(context, sink, ct)).ConfigureAwait(false);
            if (!sink.Done && !execution.CancelRequested)
            {
                _logger.LogWarning("Agent {Agent} ended task {TaskId} without a final state; failing it", execution.AgentName, execution.TaskId);
                await sink.FailAsync("Agent ended without a final state").ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (execution.CancelRequested)
        {
            _logger.LogDebug(ex, "Agent {Agent} stopped after task {TaskId} was canceled", execution.AgentName, execution.TaskId);
        }
        catch (Exception ex)
        {
            if (span is not null)
            {
                span.SetStatus(System.Diagnostics.ActivityStatusCode.Error);
                span.SetTag(A2ATelemetryNames.ErrorType, ex.GetType().FullName);
            }
            if (sink.Done)
            {
                _logger.LogWarning(ex, "Agent {Agent} threw after task {TaskId} reached a final state", execution.AgentName, execution.TaskId);
            }
            else
            {
                _logger.LogError(ex, "Agent {Agent} failed on task {TaskId}", execution.AgentName, execution.TaskId);
                try { await sink.FailAsync("Agent error").ConfigureAwait(false); }
                catch (Exception failEx) { _logger.LogError(failEx, "Could not fail task {TaskId}", execution.TaskId); }
            }
        }
        finally
        {
            _running.TryRemove(KeyValuePair.Create((execution.AgentName, execution.TaskId), execution));
            await CompleteFirstResultAsync(execution).ConfigureAwait(false);
            if (!execution.CancelRequested) execution.Settled.TrySetResult(); // on cancel, CancelTaskAsync settles it
            execution.Cts.Dispose(); // CancelTaskAsync tolerates ObjectDisposedException
        }
    }

    private async Task CompleteFirstResultAsync(TaskExecution execution)
    {
        if (execution.FirstResult.Task.IsCompleted) return;
        try
        {
            var stored = await _writer.Store.GetAsync(execution.AgentName, execution.TaskId, CancellationToken.None).ConfigureAwait(false);
            if (stored is not null) execution.FirstResult.TrySetResult(new SendMessageResponse { Task = stored.Task });
            else execution.FirstResult.TrySetException(new A2AException(A2AErrorCode.InternalError, "The agent produced no result."));
        }
        catch (Exception ex)
        {
            execution.FirstResult.TrySetException(ex);
        }
    }

    /// <summary>Creates the handler in its own DI scope (registered service, else ActivatorUtilities), calls it, disposes.</summary>
    private async Task InvokeHandlerAsync(AgentRegistration registration, Func<IAgentHandler, ValueTask> call)
    {
        var scope = _services.GetService<IServiceScopeFactory>()?.CreateAsyncScope();
        try
        {
            var provider = scope?.ServiceProvider ?? _services;
            var registered = provider.GetService(registration.HandlerType) as IAgentHandler;
            var handler = registered ?? (IAgentHandler)ActivatorUtilities.CreateInstance(provider, registration.HandlerType);
            try
            {
                await call(handler).ConfigureAwait(false);
            }
            finally
            {
                if (registered is null)
                {
                    if (handler is IAsyncDisposable ad) await ad.DisposeAsync().ConfigureAwait(false);
                    else (handler as IDisposable)?.Dispose();
                }
            }
        }
        finally
        {
            if (scope is { } s) await s.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<(AgentTask Snapshot, TaskEventBroker.Subscription Subscription)> OpenSubscriptionAsync(
        A2ACallContext call, string taskId, CancellationToken ct)
    {
        using (await _writer.LockAsync(call.AgentName, taskId, ct).ConfigureAwait(false))
        {
            var stored = await LoadOwnedAsync(call, taskId, ct).ConfigureAwait(false);
            var state = stored.Task.Status.State;
            if (TaskStateRules.IsTerminal(state))
                throw new A2AException(A2AErrorCode.UnsupportedOperation, $"Task '{taskId}' is {state}; a terminal task cannot be subscribed to.");
            // Registered under the lock, so no event between the snapshot and the first live event is lost or repeated.
            // Spec 3.1.6: an interrupted task can be subscribed to; the stream ends only at a terminal state.
            var subscription = _writer.Broker.Open(call.AgentName, taskId, ct, untilTerminal: true);
            return (stored.Task, subscription);
        }
    }

    private async Task<StoredTask> LoadOwnedAsync(A2ACallContext call, string taskId, CancellationToken ct)
    {
        var stored = string.IsNullOrEmpty(taskId) ? null : await _writer.Store.GetAsync(call.AgentName, taskId, ct).ConfigureAwait(false);
        // Another owner's task is reported as not found, so task ids do not leak.
        if (stored is null || stored.Owner != call.Owner)
            throw new A2AException(A2AErrorCode.TaskNotFound, $"Task '{taskId}' was not found.");
        return stored;
    }

    /// <summary>
    /// Like <see cref="LoadOwnedAsync"/>, but another owner may continue the task when <see cref="IOwnerResolver.CanContinueInterrupted"/>
    /// allows it (default: step-up of an AUTH_REQUIRED task within one tenant). The task keeps its owner.
    /// </summary>
    private async Task<StoredTask> LoadForContinuationAsync(A2ACallContext call, string taskId, CancellationToken ct)
    {
        var stored = string.IsNullOrEmpty(taskId) ? null : await _writer.Store.GetAsync(call.AgentName, taskId, ct).ConfigureAwait(false);
        if (stored is null
            || (stored.Owner != call.Owner && !_owners.CanContinueInterrupted(stored.Owner, call.Owner, stored.Task.Status.State)))
            throw new A2AException(A2AErrorCode.TaskNotFound, $"Task '{taskId}' was not found.");
        return stored;
    }

    private AgentRegistration GetAgent(A2ACallContext call)
        => _catalog.TryGet(call.AgentName, out var registration)
            ? registration
            : throw new InvalidOperationException($"Unknown agent '{call.AgentName}'. Transports must reject unknown agents (HTTP 404) before calling the handler.");

    private static void RequireStreaming(AgentRegistration registration)
    {
        if (registration.PublicCard.Capabilities.Streaming != true)
            throw new A2AException(A2AErrorCode.UnsupportedOperation, "This agent does not support streaming.");
    }

    private static void RequirePush(AgentRegistration registration)
    {
        if (registration.PublicCard.Capabilities.PushNotifications != true)
            throw new A2AException(A2AErrorCode.PushNotificationNotSupported, "This agent does not support push notifications.");
    }
}
