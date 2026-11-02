using System.Text.Json;
using A2A.Protocol;
using A2A.Server.Agents;
using A2A.Server.Hosting;
using A2A.Server.Transports.Sse;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace A2A.Server.Transports.JsonRpc;

/// <summary>
/// <c>POST /agents/{agent}/jsonrpc</c>: a thin JSON-RPC 2.0 adapter over <see cref="IA2ARequestHandler"/>.
/// Order of checks: unknown agent (HTTP 404) → agent policy (HTTP 401/403) → body size (HTTP 413) → JSON (-32700) →
/// envelope (-32600) → version (-32009) → method (-32601) → extended card needs an authenticated caller (HTTP 401) →
/// params (-32602) → handler. Every JSON-RPC error is sent with HTTP 200.
/// A request without <c>id</c> is processed and answered with <c>"id": null</c>; batches are rejected (-32600).
/// Streaming methods answer <c>text/event-stream</c> once the first event exists; an error before that is a plain
/// JSON-RPC error response, an error after it is one final SSE frame carrying <c>error</c>.
/// </summary>
internal static class JsonRpcEndpoint
{
    private const string LoggerName = "A2A.Server.JsonRpc";

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
            with { RequiresAuthentication = true },
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
            var owner = await TransportRequest.AuthorizeAsync(http, agent, requireAuthenticated: false).ConfigureAwait(false);

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
                await WriteJsonAsync(http, JsonRpcMessages.Error(default, new A2AException(A2AErrorCode.ParseError, "The body is not valid JSON."))).ConfigureAwait(false);
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

            await DispatchAsync(http, agent, owner, request, logger, ct).ConfigureAwait(false);
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

    private static async Task DispatchAsync(HttpContext http, AgentRegistration agent, string owner, JsonRpcRequest request,
        ILogger logger, CancellationToken ct)
    {
        Method method;
        object parameters;
        try
        {
            var (version, extensions) = ServiceParameters.Read(http);
            ServiceParameters.EnsureSupportedVersion(version);
            if (!Methods.TryGetValue(request.Method, out method!))
                throw new A2AException(A2AErrorCode.MethodNotFound, $"Method '{request.Method}' does not exist.");
            if (method.RequiresAuthentication)
                await TransportRequest.AuthorizeAsync(http, agent, requireAuthenticated: true).ConfigureAwait(false);

            var p = request.Params.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? EmptyObject : request.Params;
            parameters = TransportErrors.Deserialize(p, method.ParamsType);
            RequestValidation.Validate(parameters);
            var call = TransportRequest.CreateCallContext(http, agent, owner, version, extensions, parameters);
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
            if (http.Response.HasStarted) return; // SSE already started; StreamAsync reports its own errors
            await WriteJsonAsync(http, JsonRpcMessages.Error(request.Id, error)).ConfigureAwait(false);
        }
    }

    private static async Task WriteJsonAsync(HttpContext http, byte[] json)
    {
        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = A2AConstants.ContentType;
        await http.Response.Body.WriteAsync(json, http.RequestAborted).ConfigureAwait(false);
    }

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    private static Method Unary<TRequest, TResult>(string name, Func<IA2ARequestHandler, A2ACallContext, TRequest, CancellationToken, Task<TResult>> invoke)
        => new(name, typeof(TRequest), typeof(TResult), Invoke: async (h, c, r, ct) => await invoke(h, c, (TRequest)r, ct).ConfigureAwait(false));

    private static Method Streaming<TRequest>(string name, Func<IA2ARequestHandler, A2ACallContext, TRequest, CancellationToken, IAsyncEnumerable<StreamResponse>> stream)
        => new(name, typeof(TRequest), typeof(StreamResponse), Stream: (h, c, r, ct) => stream(h, c, (TRequest)r, ct));

    /// <param name="ResultType">Serialized result type; null → <c>{}</c>.</param>
    private sealed record Method(
        string Name,
        Type ParamsType,
        Type? ResultType,
        Func<IA2ARequestHandler, A2ACallContext, object, CancellationToken, Task<object?>>? Invoke = null,
        Func<IA2ARequestHandler, A2ACallContext, object, CancellationToken, IAsyncEnumerable<StreamResponse>>? Stream = null)
    {
        public bool RequiresAuthentication { get; init; }
    }
}
