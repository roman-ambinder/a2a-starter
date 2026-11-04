using System.Runtime.CompilerServices;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.Server;

/// <summary>
/// The <see cref="IA2ARequestHandler"/> every transport and every in-process caller uses. It runs the middleware pipeline and then
/// the <see cref="TaskManager"/>. Nothing calls the task manager directly.
/// </summary>
internal sealed class A2APipelineRequestHandler : IA2ARequestHandler
{
    private readonly IServiceProvider _root;
    private readonly A2APipeline _pipeline;

    public A2APipelineRequestHandler(IServiceProvider root, TaskManager tasks, AgentCatalog catalog, A2APipelineMiddleware middleware)
    {
        _root = root;
        _pipeline = new A2APipeline(tasks, catalog, middleware);
    }

    public Task<SendMessageResponse> SendMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct)
        => UnaryAsync<SendMessageResponse>(A2AOperation.SendMessage, call, request, ct);

    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct)
        => StreamAsync(A2AOperation.SendStreamingMessage, call, request, ct);

    public Task<AgentTask> GetTaskAsync(A2ACallContext call, GetTaskRequest request, CancellationToken ct)
        => UnaryAsync<AgentTask>(A2AOperation.GetTask, call, request, ct);

    public Task<ListTasksResponse> ListTasksAsync(A2ACallContext call, ListTasksRequest request, CancellationToken ct)
        => UnaryAsync<ListTasksResponse>(A2AOperation.ListTasks, call, request, ct);

    public Task<AgentTask> CancelTaskAsync(A2ACallContext call, CancelTaskRequest request, CancellationToken ct)
        => UnaryAsync<AgentTask>(A2AOperation.CancelTask, call, request, ct);

    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(A2ACallContext call, SubscribeToTaskRequest request, CancellationToken ct)
        => StreamAsync(A2AOperation.SubscribeToTask, call, request, ct);

    public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(A2ACallContext call, TaskPushNotificationConfig request, CancellationToken ct)
        => UnaryAsync<TaskPushNotificationConfig>(A2AOperation.CreateTaskPushNotificationConfig, call, request, ct);

    public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(A2ACallContext call, GetTaskPushNotificationConfigRequest request, CancellationToken ct)
        => UnaryAsync<TaskPushNotificationConfig>(A2AOperation.GetTaskPushNotificationConfig, call, request, ct);

    public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(A2ACallContext call, ListTaskPushNotificationConfigsRequest request, CancellationToken ct)
        => UnaryAsync<ListTaskPushNotificationConfigsResponse>(A2AOperation.ListTaskPushNotificationConfigs, call, request, ct);

    public async Task DeleteTaskPushNotificationConfigAsync(A2ACallContext call, DeleteTaskPushNotificationConfigRequest request, CancellationToken ct)
        => await RunUnaryAsync(A2AOperation.DeleteTaskPushNotificationConfig, call, request, ct).ConfigureAwait(false);

    public Task<AgentCard> GetExtendedAgentCardAsync(A2ACallContext call, GetExtendedAgentCardRequest request, CancellationToken ct)
        => UnaryAsync<AgentCard>(A2AOperation.GetExtendedAgentCard, call, request, ct);

    private async Task<T> UnaryAsync<T>(A2AOperation operation, A2ACallContext call, object request, CancellationToken ct) where T : class
    {
        var value = await RunUnaryAsync(operation, call, request, ct).ConfigureAwait(false);
        return value as T ?? throw new InvalidOperationException(
            $"The pipeline for {operation} produced no {typeof(T).Name}. A middleware must return the result of next, or a {typeof(T).Name}.");
    }

    private async Task<object?> RunUnaryAsync(A2AOperation operation, A2ACallContext call, object request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(request);
        var scope = call.Services is null ? _root.CreateAsyncScope() : (AsyncServiceScope?)null;
        try
        {
            var context = CreateContext(operation, call, request, scope?.ServiceProvider ?? call.Services!, ct);
            var result = await _pipeline.RunAsync(context).ConfigureAwait(false);
            if (result.Events is not null)
                throw new InvalidOperationException($"The pipeline for {operation} produced an event stream, but {operation} returns one value.");
            return result.Value;
        }
        finally
        {
            if (scope is { } s) await s.DisposeAsync().ConfigureAwait(false);
        }
    }

    // An async iterator: the chain runs on the first MoveNextAsync, so errors surface there, before any event.
    private async IAsyncEnumerable<StreamResponse> StreamAsync(A2AOperation operation, A2ACallContext call, object request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(request);
        var scope = call.Services is null ? _root.CreateAsyncScope() : (AsyncServiceScope?)null;
        try
        {
            var context = CreateContext(operation, call, request, scope?.ServiceProvider ?? call.Services!, ct);
            var result = await _pipeline.RunAsync(context).ConfigureAwait(false);
            var events = result.Events ?? throw new InvalidOperationException($"The pipeline for {operation} produced no event stream. A middleware must return the result of next, or a stream.");
            await foreach (var item in events.WithCancellation(ct).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            if (scope is { } s) await s.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static A2AOperationContext CreateContext(A2AOperation operation, A2ACallContext call, object request, IServiceProvider services, CancellationToken ct)
        => new(operation, call, request, services, call.ResponseMetadata ?? new A2AResponseMetadata(), ct);
}
