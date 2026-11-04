using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.Server;

/// <summary>Middleware registered through the builder, shared by all agents. Built once at startup.</summary>
internal sealed class A2APipelineMiddleware
{
    public const int TracingSlot = 100;
    public const int LimitsSlot = 200;
    public const int AuthorizationSlot = 300;
    public const int ExtensionsSlot = 400;

    private readonly List<(int Slot, Type Type)> _builtIn = [];
    private readonly List<Type> _global = [];

    public IReadOnlyList<(int Slot, Type Type)> BuiltIn => _builtIn;

    public IReadOnlyList<Type> Global => _global;

    public void AddBuiltIn(int slot, Type type) => _builtIn.Add((slot, type));

    public bool HasBuiltIn(int slot) => _builtIn.Any(b => b.Slot == slot);

    public void AddGlobal(Type type) => _global.Add(type);
}

/// <summary>Builds the middleware chains, one pair per agent, once. Runs a chain for one operation.</summary>
internal sealed class A2APipeline
{
    private readonly Chain _fallback;
    private readonly Dictionary<string, Chain> _agents = new(StringComparer.Ordinal);

    public A2APipeline(TaskManager tasks, AgentCatalog catalog, A2APipelineMiddleware middleware)
    {
        var terminal = Terminal(tasks);
        var builtIn = middleware.BuiltIn.OrderBy(b => b.Slot).ToArray(); // stable: equal slots keep registration order
        _fallback = Create(terminal, builtIn, middleware.Global, []);
        foreach (var agent in catalog.All)
            _agents[agent.Name] = Create(terminal, builtIn, middleware.Global, agent.MiddlewareTypes);
    }

    public ValueTask<A2AOperationResult> RunAsync(A2AOperationContext context)
    {
        var chain = _agents.TryGetValue(context.Call.AgentName, out var c) ? c : _fallback;
        return (context.Call.IsForwarded ? chain.Forwarded : chain.Full)(context);
    }

    private static Chain Create(A2AOperationDelegate terminal, (int Slot, Type Type)[] builtIn, IReadOnlyList<Type> global, IReadOnlyList<Type> perAgent)
    {
        var full = builtIn.Select(b => b.Type).Concat(global).Concat(perAgent).ToArray();
        // B5: a forwarded call runs only the tracing slot, then the operation.
        var forwarded = builtIn.Where(b => b.Slot == A2APipelineMiddleware.TracingSlot).Select(b => b.Type).ToArray();
        return new Chain(Build(terminal, full), Build(terminal, forwarded));
    }

    private static A2AOperationDelegate Build(A2AOperationDelegate terminal, Type[] types)
    {
        var next = terminal;
        for (var i = types.Length - 1; i >= 0; i--)
        {
            var type = types[i];
            var inner = next;
            next = context => ((IA2AMiddleware)context.Services.GetRequiredService(type)).InvokeAsync(context, inner);
        }
        return next;
    }

    private static A2AOperationDelegate Terminal(TaskManager t) => async context =>
    {
        var c = context.Call;
        var ct = context.RequestAborted;
        switch (context.Operation)
        {
            case A2AOperation.SendMessage:
                return A2AOperationResult.FromValue(await t.SendMessageAsync(c, Req<SendMessageRequest>(context), ct).ConfigureAwait(false));
            case A2AOperation.SendStreamingMessage:
                return A2AOperationResult.FromEvents(t.SendStreamingMessageAsync(c, Req<SendMessageRequest>(context), ct));
            case A2AOperation.GetTask:
                return A2AOperationResult.FromValue(await t.GetTaskAsync(c, Req<GetTaskRequest>(context), ct).ConfigureAwait(false));
            case A2AOperation.ListTasks:
                return A2AOperationResult.FromValue(await t.ListTasksAsync(c, Req<ListTasksRequest>(context), ct).ConfigureAwait(false));
            case A2AOperation.CancelTask:
                return A2AOperationResult.FromValue(await t.CancelTaskAsync(c, Req<CancelTaskRequest>(context), ct).ConfigureAwait(false));
            case A2AOperation.SubscribeToTask:
                return A2AOperationResult.FromEvents(t.SubscribeToTaskAsync(c, Req<SubscribeToTaskRequest>(context), ct));
            case A2AOperation.CreateTaskPushNotificationConfig:
                return A2AOperationResult.FromValue(await t.CreateTaskPushNotificationConfigAsync(c, Req<TaskPushNotificationConfig>(context), ct).ConfigureAwait(false));
            case A2AOperation.GetTaskPushNotificationConfig:
                return A2AOperationResult.FromValue(await t.GetTaskPushNotificationConfigAsync(c, Req<GetTaskPushNotificationConfigRequest>(context), ct).ConfigureAwait(false));
            case A2AOperation.ListTaskPushNotificationConfigs:
                return A2AOperationResult.FromValue(await t.ListTaskPushNotificationConfigsAsync(c, Req<ListTaskPushNotificationConfigsRequest>(context), ct).ConfigureAwait(false));
            case A2AOperation.DeleteTaskPushNotificationConfig:
                await t.DeleteTaskPushNotificationConfigAsync(c, Req<DeleteTaskPushNotificationConfigRequest>(context), ct).ConfigureAwait(false);
                return A2AOperationResult.FromValue(null);
            case A2AOperation.GetExtendedAgentCard:
                return A2AOperationResult.FromValue(await t.GetExtendedAgentCardAsync(c, Req<GetExtendedAgentCardRequest>(context), ct).ConfigureAwait(false));
            default:
                throw new InvalidOperationException($"The pipeline does not know the operation {context.Operation}.");
        }
    };

    /// <summary>Returns the request when it still has the expected type; throws otherwise.</summary>
    private static T Req<T>(A2AOperationContext context)
        => context.Request is T request
            ? request
            : throw new InvalidOperationException(
                $"Middleware replaced the request for {context.Operation} with {context.Request.GetType().Name}; it must stay {typeof(T).Name}.");

    private sealed record Chain(A2AOperationDelegate Full, A2AOperationDelegate Forwarded);
}
