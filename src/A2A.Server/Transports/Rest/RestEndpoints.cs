using System.Text.Json;
using System.Text.Json.Nodes;
using A2A.Protocol;
using A2A.Server.Agents;
using A2A.Server.Hosting;
using A2A.Server.Transports.Sse;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Kind = A2A.Server.Transports.Rest.RestQuery.Kind;

namespace A2A.Server.Transports.Rest;

/// <summary>
/// HTTP+JSON binding under <c>/agents/{agent}/rest</c>: a thin adapter over <see cref="IA2ARequestHandler"/>.
/// One catch-all route per verb parses the path itself, because the public paths contain <c>:</c> (<c>message:send</c>,
/// <c>{id}:cancel</c>) next to plain <c>{id}</c> routes. Order of checks: unknown agent (404) → agent policy (401/403) →
/// path and verb (404/405) → version (400) → extended card needs an authenticated caller (401) → body size (413) →
/// body and query (400) → required fields (400) → handler. Errors are google.rpc.Status bodies.
/// A streaming error before the first event is a plain error response; after it, one final <c>data: {"error":..}</c> frame.
/// </summary>
internal static class RestEndpoints
{
    private const string LoggerName = "A2A.Server.Rest";

    private const string CancelSuffix = ":cancel";
    private const string SubscribeSuffix = ":subscribe";

    private enum Op { Send, Stream, GetTask, ListTasks, Cancel, Subscribe, CreatePush, GetPush, ListPush, DeletePush, ExtendedCard }

    private sealed record Route(Op Op, string TaskId = "", string ConfigId = "");

    private static readonly Dictionary<string, Kind> GetTaskQuery = new() { ["historyLength"] = Kind.Int };

    private static readonly Dictionary<string, Kind> ListTasksQuery = new()
    {
        ["contextId"] = Kind.String,
        ["status"] = Kind.Enum,
        ["pageSize"] = Kind.Int,
        ["pageToken"] = Kind.String,
        ["historyLength"] = Kind.Int,
        ["statusTimestampAfter"] = Kind.Timestamp,
        ["includeArtifacts"] = Kind.Bool,
    };

    private static readonly Dictionary<string, Kind> ListPushQuery = new() { ["pageSize"] = Kind.Int, ["pageToken"] = Kind.String };
    private static readonly Dictionary<string, Kind> NoQuery = [];

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
            await DispatchAsync(http, agent, owner, options, logger, ct).ConfigureAwait(false);
        }
        catch (A2AAuthException ex)
        {
            await TransportRequest.WriteAuthFailureAsync(http, ex).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client went away; nothing to answer.
        }
        catch (Exception ex)
        {
            await RestErrorWriter.Write(http, TransportErrors.ToA2AException(ex, logger)).ConfigureAwait(false);
        }
    }

    private static async Task DispatchAsync(HttpContext http, AgentRegistration agent, string owner, A2AServerOptions options,
        ILogger logger, CancellationToken ct)
    {
        var path = http.Request.RouteValues["path"] as string ?? "";
        var route = Resolve(http.Request.Method, path, out var allowed);
        if (route is null)
        {
            if (allowed is not null)
            {
                http.Response.Headers.Allow = string.Join(", ", allowed);
                await TransportErrors.WriteStatusAsync(http, StatusCodes.Status405MethodNotAllowed, "UNIMPLEMENTED",
                    $"Method {http.Request.Method} is not allowed here.").ConfigureAwait(false);
                return;
            }
            throw new A2AException(A2AErrorCode.MethodNotFound, $"No operation at '/{path}'.");
        }

        var (version, extensions) = ServiceParameters.Read(http);
        ServiceParameters.EnsureSupportedVersion(version);
        if (route.Op == Op.ExtendedCard)
            await TransportRequest.AuthorizeAsync(http, agent, requireAuthenticated: true).ConfigureAwait(false);

        JsonObject? body = null;
        if (route.Op is Op.Send or Op.Stream or Op.Cancel or Op.CreatePush)
        {
            var bytes = await RequestBody.ReadAsync(http, options.MaxRequestBodyBytes).ConfigureAwait(false);
            if (bytes is null)
            {
                RequestBody.WriteTooLarge(http);
                return;
            }
            body = ParseBody(bytes, required: route.Op != Op.Cancel);
        }

        var request = BuildRequest(route, body, http.Request.Query);
        RequestValidation.Validate(request);
        var call = TransportRequest.CreateCallContext(http, agent, owner, version, extensions, request);
        var handler = http.RequestServices.GetRequiredService<IA2ARequestHandler>();

        switch (route.Op)
        {
            case Op.Stream:
                await StreamAsync(http, handler.SendStreamingMessageAsync(call, (SendMessageRequest)request, ct), logger, ct).ConfigureAwait(false);
                return;
            case Op.Subscribe:
                await StreamAsync(http, handler.SubscribeToTaskAsync(call, (SubscribeToTaskRequest)request, ct), logger, ct).ConfigureAwait(false);
                return;
            case Op.DeletePush:
                await handler.DeleteTaskPushNotificationConfigAsync(call, (DeleteTaskPushNotificationConfigRequest)request, ct).ConfigureAwait(false);
                http.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
        }

        var (result, type) = route.Op switch
        {
            Op.Send => ((object)await handler.SendMessageAsync(call, (SendMessageRequest)request, ct).ConfigureAwait(false), typeof(SendMessageResponse)),
            Op.GetTask => (await handler.GetTaskAsync(call, (GetTaskRequest)request, ct).ConfigureAwait(false), typeof(AgentTask)),
            Op.ListTasks => (await handler.ListTasksAsync(call, (ListTasksRequest)request, ct).ConfigureAwait(false), typeof(ListTasksResponse)),
            Op.Cancel => (await handler.CancelTaskAsync(call, (CancelTaskRequest)request, ct).ConfigureAwait(false), typeof(AgentTask)),
            Op.CreatePush => (await handler.CreateTaskPushNotificationConfigAsync(call, (TaskPushNotificationConfig)request, ct).ConfigureAwait(false), typeof(TaskPushNotificationConfig)),
            Op.GetPush => (await handler.GetTaskPushNotificationConfigAsync(call, (GetTaskPushNotificationConfigRequest)request, ct).ConfigureAwait(false), typeof(TaskPushNotificationConfig)),
            Op.ListPush => (await handler.ListTaskPushNotificationConfigsAsync(call, (ListTaskPushNotificationConfigsRequest)request, ct).ConfigureAwait(false), typeof(ListTaskPushNotificationConfigsResponse)),
            Op.ExtendedCard => (await handler.GetExtendedAgentCardAsync(call, (GetExtendedAgentCardRequest)request, ct).ConfigureAwait(false), typeof(AgentCard)),
            _ => throw new InvalidOperationException($"Unhandled operation {route.Op}."),
        };

        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = A2AConstants.ContentType;
        await http.Response.Body.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(result, A2AJson.Options.GetTypeInfo(type)), ct).ConfigureAwait(false);
    }

    private static Task StreamAsync(HttpContext http, IAsyncEnumerable<StreamResponse> events, ILogger logger, CancellationToken ct)
        => SseStreaming.RunAsync(http, events,
            e => JsonSerializer.SerializeToUtf8Bytes(e, A2AJson.Options.GetTypeInfo(typeof(StreamResponse))),
            RestErrorWriter.ToJson, logger, ct);

    // ---------- path ----------

    /// <summary>Matches the path and verb. Null with <paramref name="allowed"/> set → path known, verb wrong (405). Null with no list → unknown path.</summary>
    private static Route? Resolve(string method, string path, out string[]? allowed)
    {
        allowed = null;
        var s = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var verbs = new Dictionary<string, Route>(StringComparer.OrdinalIgnoreCase);

        switch (s)
        {
            case ["message:send"]: verbs["POST"] = new(Op.Send); break;
            case ["message:stream"]: verbs["POST"] = new(Op.Stream); break;
            case ["tasks"]: verbs["GET"] = new(Op.ListTasks); break;
            case ["extendedAgentCard"]: verbs["GET"] = new(Op.ExtendedCard); break;
            case ["tasks", var seg] when seg.EndsWith(CancelSuffix, StringComparison.Ordinal):
                verbs["POST"] = new(Op.Cancel, seg.Substring(0, seg.Length - CancelSuffix.Length));
                break;
            case ["tasks", var seg] when seg.EndsWith(SubscribeSuffix, StringComparison.Ordinal):
                var subscribe = new Route(Op.Subscribe, seg.Substring(0, seg.Length - SubscribeSuffix.Length));
                verbs["GET"] = subscribe;
                verbs["POST"] = subscribe;
                break;
            case ["tasks", var id]: verbs["GET"] = new(Op.GetTask, id); break;
            case ["tasks", var t, "pushNotificationConfigs"]:
                verbs["POST"] = new(Op.CreatePush, t);
                verbs["GET"] = new(Op.ListPush, t);
                break;
            case ["tasks", var t, "pushNotificationConfigs", var c]:
                verbs["GET"] = new(Op.GetPush, t, c);
                verbs["DELETE"] = new(Op.DeletePush, t, c);
                break;
            default: return null;
        }

        if (verbs.TryGetValue(method, out var route)) return route;
        allowed = verbs.Keys.ToArray();
        return null;
    }

    // ---------- request ----------

    private static JsonObject? ParseBody(byte[] bytes, bool required)
    {
        if (bytes.Length == 0 || bytes.All(b => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
        {
            if (required) throw new A2AException(A2AErrorCode.InvalidParams, "Invalid params: a JSON body is required.");
            return null;
        }
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(bytes);
        }
        catch (JsonException)
        {
            throw new A2AException(A2AErrorCode.ParseError, "The body is not valid JSON.");
        }
        return node as JsonObject ?? throw new A2AException(A2AErrorCode.InvalidParams, "Invalid params: the body must be a JSON object.");
    }

    private static object BuildRequest(Route route, JsonObject? body, IQueryCollection query)
    {
        switch (route.Op)
        {
            case Op.Send:
            case Op.Stream:
                return Deserialize<SendMessageRequest>(body!);
            case Op.Cancel:
                body ??= [];
                body["id"] = route.TaskId; // the path wins
                return Deserialize<CancelTaskRequest>(body);
            case Op.CreatePush:
                if (body!.TryGetPropertyValue("taskId", out var bodyTask) && bodyTask is not null
                    && !(bodyTask is JsonValue v && v.TryGetValue<string>(out var s) && (s.Length == 0 || s == route.TaskId)))
                    throw new A2AException(A2AErrorCode.InvalidParams, "Invalid params: taskId in the body differs from the path.");
                body["taskId"] = route.TaskId;
                return Deserialize<TaskPushNotificationConfig>(body);
            case Op.GetTask:
                return RestQuery.Bind<GetTaskRequest>(query, GetTaskQuery, ("id", route.TaskId));
            case Op.ListTasks:
                return RestQuery.Bind<ListTasksRequest>(query, ListTasksQuery);
            case Op.Subscribe:
                return RestQuery.Bind<SubscribeToTaskRequest>(query, NoQuery, ("id", route.TaskId));
            case Op.GetPush:
                return RestQuery.Bind<GetTaskPushNotificationConfigRequest>(query, NoQuery, ("taskId", route.TaskId), ("id", route.ConfigId));
            case Op.DeletePush:
                return RestQuery.Bind<DeleteTaskPushNotificationConfigRequest>(query, NoQuery, ("taskId", route.TaskId), ("id", route.ConfigId));
            case Op.ListPush:
                return RestQuery.Bind<ListTaskPushNotificationConfigsRequest>(query, ListPushQuery, ("taskId", route.TaskId));
            case Op.ExtendedCard:
                return new GetExtendedAgentCardRequest();
            default:
                throw new InvalidOperationException($"Unhandled operation {route.Op}.");
        }
    }

    private static T Deserialize<T>(JsonObject body)
        => (T)TransportErrors.Deserialize(JsonSerializer.SerializeToElement(body), typeof(T));
}
