using Ambinder.A2A.AspNetCore.Hosting;
using System.Text.Json;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.AspNetCore.Transports.Sse;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.AspNetCore.Transports.JsonRpc;

/// <summary>
/// <c>POST /agents/{agent}/jsonrpc</c>: a thin JSON-RPC 2.0 adapter over <see cref="IA2ARequestHandler"/>.
/// Order of checks: unknown agent (HTTP 404) → agent policy and caller (HTTP 401/403) → an <see cref="IA2AVersionDispatcher"/> that claims an
/// unsupported version takes the whole request → body size (HTTP 413) → JSON (-32700) →
/// envelope (-32600) → version (-32009) → method (-32601) → extended card needs an authenticated caller (HTTP 401) →
/// params (-32602) → handler. Every JSON-RPC error is sent with HTTP 200.
/// A request without <c>id</c> is processed and answered with <c>"id": null</c>; batches are rejected (-32600).
/// Streaming methods answer <c>text/event-stream</c> once the first event exists; an error before that is a plain
/// JSON-RPC error response, an error after it is one final SSE frame carrying <c>error</c>.
/// </summary>
internal static class JsonRpcEndpoint
{
    private const string LoggerName = "Ambinder.A2A.Server.JsonRpc";

    private static readonly Dictionary<string, Method> Methods = new Method[]
    {
        Unary<SendMessageRequest, SendMessageResponse>("SendMessage", (h, c, r, ct) => h.SendMessageAsync(c, r, ct)),
        Streaming<SendMessageRequest>("SendStreamingMessage", (h, c, r, ct) => h.SendStreamingMessageAsync(c, r, ct)),
        Unary<GetTaskRequest, AgentTask>("GetTask", (h, c, r, ct) => h.GetTaskAsync(c, r, ct)),
        Unary<ListTasksRequest, ListTasksResponse>("ListTasks", (h, c, r, ct) => h.ListTasksAsync(c, r, ct)),
        Unary<CancelTaskRequest, AgentTask>("CancelTask", (h, c, r, ct) => h.CancelTaskAsync(c, r, ct)),
        Streaming<SubscribeToTaskRequest>("SubscribeToTask", (h, c, r, ct) => h.SubscribeToTaskAsync(c, r, ct)),
        Unary<TaskPushNotificationConfig, TaskPushNotificationConfig>("CreateTaskPushNotificationConfig",
            (h, c, r, ct) => h.CreateTaskPushNotificationConfigAsync(c, r, ct)),
        Unary<GetTaskPushNotificationConfigRequest, TaskPushNotificationConfig>("GetTaskPushNotificationConfig",
            (h, c, r, ct) => h.GetTaskPushNotificationConfigAsync(c, r, ct)),
        Unary<ListTaskPushNotificationConfigsRequest, ListTaskPushNotificationConfigsResponse>("ListTaskPushNotificationConfigs",
            (h, c, r, ct) => h.ListTaskPushNotificationConfigsAsync(c, r, ct)),
        new("DeleteTaskPushNotificationConfig", typeof(DeleteTaskPushNotificationConfigRequest), ResultType: null,
            Invoke: async (h, c, r, ct) =>
            {
                await h.DeleteTaskPushNotificationConfigAsync(c, (DeleteTaskPushNotificationConfigRequest)r, ct).ConfigureAwait(false);
                return null;
            }),
        Unary<GetExtendedAgentCardRequest, AgentCard>("GetExtendedAgentCard", (h, c, r, ct) => h.GetExtendedAgentCardAsync(c, r, ct))
            with { IsExtendedCard = true },
    }.ToDictionary(m => m.Name, StringComparer.Ordinal);

    public static async Task HandleAsync(HttpContext http)
    {
        var services = http.RequestServices;
        var options = services.GetRequiredService<IOptions<A2AServerOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerName);
        var ct = http.RequestAborted;

        var agentName = http.Request.RouteValues["agent"] as string;
        if (agentName is null || !services.GetRequiredService<AgentCatalog>().TryGet(agentName, out var agent))
        {
            await TransportErrors.WriteUnknownAgentAsync(http, agentName).ConfigureAwait(false);
            return;
        }

        try
        {
            var (handled, caller) = await TransportRequest.TryDispatchVersionAsync(http, agent, A2AConstants.BindingJsonRpc, default)
                .ConfigureAwait(false);
            if (handled) return;

            var body = await RequestBody.ReadAsync(http, options.MaxRequestBodyBytes).ConfigureAwait(false);
            if (body is null)
            {
                RequestBody.WriteTooLarge(http);
                return;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                await WriteJsonAsync(http, JsonRpcMessages.Error(default, new A2AException(A2AErrorCode.ParseError, "The body is not valid JSON. Send one JSON-RPC request object."))).ConfigureAwait(false);
                return;
            }

            JsonRpcRequest request;
            using (document)
            {
                if (!JsonRpcMessages.TryParse(document.RootElement, out request, out var badId, out var envelopeError))
                {
                    await WriteJsonAsync(http, JsonRpcMessages.Error(badId, envelopeError!)).ConfigureAwait(false);
                    return;
                }
            }

            await DispatchAsync(http, agent, caller, request, logger, ct).ConfigureAwait(false);
        }
        catch (A2AAuthException ex)
        {
            await TransportRequest.WriteAuthFailureAsync(http, ex).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client went away; nothing to answer.
        }
    }

    private static async Task DispatchAsync(HttpContext http, AgentRegistration agent, DispatchCaller caller, JsonRpcRequest request,
        ILogger logger, CancellationToken ct)
    {
        Method method;
        object parameters;
        try
        {
            var (version, extensions) = ServiceParameters.Read(http);
            TransportRequest.EnsureVersion(version);
            if (!Methods.TryGetValue(request.Method, out method!))
                throw new A2AException(A2AErrorCode.MethodNotFound, $"Method '{request.Method}' does not exist. Use a method from the A2A specification.");
            if (method.IsExtendedCard)
            {
                TransportRequest.AuthorizeExtendedCard(agent, caller);
                http.Response.Headers.CacheControl = "private, no-store";
            }
            ServiceParameters.ActivateExtensions(http, agent.PublicCard, extensions);

            var p = request.Params.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? EmptyObject : request.Params;
            parameters = TransportErrors.Deserialize(p, method.ParamsType);
            RequestValidation.Validate(parameters);
            var call = TransportRequest.CreateCallContext(http, agent, caller, version, extensions, parameters, A2ATelemetryNames.TransportJsonRpc);
            var handler = http.RequestServices.GetRequiredService<IA2ARequestHandler>();

            if (method.Stream is { } stream)
            {
                var id = request.Id;
                await SseStreaming.RunAsync(http, stream(handler, call, parameters, ct),
                    e => JsonRpcMessages.Result(id, e, typeof(StreamResponse)), err => JsonRpcMessages.Error(id, err), logger, ct).ConfigureAwait(false);
                return;
            }

            var result = await method.Invoke!(handler, call, parameters, ct).ConfigureAwait(false);
            await WriteJsonAsync(http, JsonRpcMessages.Result(request.Id, result, method.ResultType)).ConfigureAwait(false);
        }
        catch (A2AAuthException)
        {
            throw; // HTTP 401/403, handled by HandleAsync
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = TransportErrors.ToA2AException(ex, logger);
            if (http.Response.HasStarted) return; // SSE already started; SseStreaming.RunAsync reports its own errors
            await WriteJsonAsync(http, JsonRpcMessages.Error(request.Id, error)).ConfigureAwait(false);
        }
    }

    private static async Task WriteJsonAsync(HttpContext http, byte[] json)
    {
        TransportRequest.WriteResponseMetadata(http);
        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = A2AConstants.JsonRpcContentType;
        await http.Response.Body.WriteAsync(json, http.RequestAborted).ConfigureAwait(false);
    }

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    private static Method Unary<TRequest, TResult>(string name, Func<IA2ARequestHandler, A2ACallContext, TRequest, CancellationToken, Task<TResult>> invoke)
        => new(name, typeof(TRequest), typeof(TResult), Invoke: async (h, c, r, ct) => await invoke(h, c, (TRequest)r, ct).ConfigureAwait(false));

    private static Method Streaming<TRequest>(string name, Func<IA2ARequestHandler, A2ACallContext, TRequest, CancellationToken, IAsyncEnumerable<StreamResponse>> stream)
        => new(name, typeof(TRequest), typeof(StreamResponse), Stream: (h, c, r, ct) => stream(h, c, (TRequest)r, ct));

    /// <param name="Name">The JSON-RPC method name.</param>
    /// <param name="ParamsType">The type of the request parameters.</param>
    /// <param name="ResultType">Serialized result type; null → <c>{}</c>.</param>
    /// <param name="Invoke">Runs a unary method, or null for a streaming one.</param>
    /// <param name="Stream">Runs a streaming method, or null for a unary one.</param>
    private sealed record Method(
        string Name,
        Type ParamsType,
        Type? ResultType,
        Func<IA2ARequestHandler, A2ACallContext, object, CancellationToken, Task<object?>>? Invoke = null,
        Func<IA2ARequestHandler, A2ACallContext, object, CancellationToken, IAsyncEnumerable<StreamResponse>>? Stream = null)
    {
        public bool IsExtendedCard { get; init; }
    }
}
