using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ambinder.A2A.AspNetCore.Transports;

/// <summary>Exception → A2A error translation and shared HTTP error bodies. Used by every transport.</summary>
internal static partial class TransportErrors
{
    /// <summary>
    /// <see cref="A2AException"/> as is; anything else → logged, <see cref="A2AErrorCode.InternalError"/> with a generic
    /// message (no internals leak). Payload errors are turned into A2A errors earlier, by <see cref="Deserialize"/>.
    /// <see cref="A2AAuthException"/> is not an A2A error: callers handle it before this.
    /// </summary>
    public static A2AException ToA2AException(Exception ex, ILogger logger)
        => ex as A2AException ?? Internal(ex, logger);

    /// <summary>Deserializes a request payload; any serializer failure → <paramref name="onError"/> (InvalidParams or ParseError).</summary>
    public static object Deserialize(JsonElement element, Type type, A2AErrorCode onError = A2AErrorCode.InvalidParams)
    {
        try
        {
            return JsonSerializer.Deserialize(element, A2AJson.Options.GetTypeInfo(type))
                   ?? throw new A2AException(onError, "The request payload must be a JSON object. Send a JSON object.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException or InvalidOperationException)
        {
            throw new A2AException(onError, $"Invalid params: {ex.Message} Fix the field named above and send the request again.");
        }
    }

    /// <summary>google.rpc.Status status name for the catalog's gRPC code, for example <c>NotFound</c> → <c>NOT_FOUND</c>.</summary>
    public static string StatusName(string grpcStatus)
    {
        var sb = new StringBuilder(grpcStatus.Length + 4);
        for (var i = 0; i < grpcStatus.Length; i++)
        {
            var c = grpcStatus[i];
            if (i > 0 && char.IsUpper(c)) sb.Append('_');
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>Writes a google.rpc.Status-shaped body: <c>{"error":{"code":..,"status":"..","message":".."}}</c>.</summary>
    public static async Task WriteStatusAsync(HttpContext http, int httpStatus, string status, string message)
    {
        http.Response.StatusCode = httpStatus;
        http.Response.ContentType = "application/json";
        using var buffer = new MemoryStream();
        var w = new Utf8JsonWriter(buffer);
        await using (w.ConfigureAwait(false))
        {
            w.WriteStartObject();
            w.WriteStartObject("error");
            w.WriteNumber("code", httpStatus);
            w.WriteString("status", status);
            w.WriteString("message", message);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        await http.Response.Body.WriteAsync(buffer.ToArray(), http.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// ErrorInfo <c>metadata</c> (a string map) from <see cref="A2AException.Data"/>: the members of a JSON object, strings as is,
    /// arrays joined with <c>,</c>, anything else as raw JSON. Not an object → nothing. Used by REST and gRPC alike.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> ErrorMetadata(JsonElement? data) => ErrorDetailsFlattener.Flatten(data);

    /// <summary>
    /// ErrorInfo reason of the server-generated internal error (<see cref="TraceIdInternalError"/>). <see cref="A2AErrorCode.InternalError"/>
    /// has no catalog reason, so this is the only place the reason exists. It follows the catalog naming (no <c>_ERROR</c> suffix).
    /// </summary>
    public const string InternalReason = "INTERNAL";

    /// <summary>
    /// The ErrorInfo reason to send for <paramref name="error"/>: the catalog reason, or <see cref="InternalReason"/> for the
    /// error <see cref="Internal"/> builds. An <see cref="A2AErrorCode.InternalError"/> a handler throws gets none, so its data
    /// stays off the wire. Null when the error gets no ErrorInfo.
    /// </summary>
    public static string? ReasonFor(A2AException error)
        => A2AError.Get(error.Code).Reason
           ?? (error is TraceIdInternalError ? InternalReason : null);

    /// <summary>The <c>@type</c> of a google.rpc.ErrorInfo detail in ProtoJSON <c>Any</c> form.</summary>
    public const string ErrorInfoType = "type.googleapis.com/google.rpc.ErrorInfo";

    /// <summary>
    /// Writes <paramref name="propertyName"/> as an array holding one google.rpc.ErrorInfo detail (<c>@type</c>, <c>reason</c>,
    /// <c>domain</c> = <c>a2a-protocol.org</c>, <c>metadata</c> from <see cref="ErrorMetadata"/>) for errors that have a catalog
    /// reason (spec 3.3.2: A2A errors MUST carry ErrorInfo). Writes nothing for the standard JSON-RPC errors.
    /// REST uses it for <c>error.details</c>, JSON-RPC for <c>error.data</c> (spec 9.5, 11.6).
    /// </summary>
    public static void WriteDetails(Utf8JsonWriter w, string propertyName, A2AException error)
    {
        var reason = ReasonFor(error);
        if (reason is null) return;
        w.WriteStartArray(propertyName);
        w.WriteStartObject();
        w.WriteString("@type", ErrorInfoType);
        w.WriteString("reason", reason);
        w.WriteString("domain", A2AError.Domain);
        if (error.Data is { ValueKind: JsonValueKind.Object })
        {
            w.WriteStartObject("metadata");
            foreach (var (name, value) in ErrorMetadata(error.Data))
                w.WriteString(name, value);
            w.WriteEndObject();
        }
        w.WriteEndObject();
        w.WriteEndArray();
    }

    /// <summary>HTTP 404 for an agent name not in the catalog.</summary>
    public static Task WriteUnknownAgentAsync(HttpContext http, string? agentName)
        => WriteStatusAsync(http, StatusCodes.Status404NotFound, "NOT_FOUND", $"Unknown agent '{agentName}'. Use an agent name from the server's agent list.");

    private static TraceIdInternalError Internal(Exception ex, ILogger logger)
    {
        var traceId = Activity.Current?.TraceId.ToHexString() ?? ActivityTraceId.CreateRandom().ToHexString();
        UnhandledTransportError(logger, ex, traceId);
        return new TraceIdInternalError(
            $"Internal error. Quote trace id {traceId} when you report it.",
            TraceIdData(traceId));
    }

    /// <summary><c>{"traceId":"..."}</c>, written by hand so it stays trim and AOT safe.</summary>
    private static JsonElement TraceIdData(string traceId)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("traceId", traceId);
            w.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>The internal error the server builds for an unknown exception. Only this one is sent with an ErrorInfo.</summary>
    private sealed class TraceIdInternalError(string message, JsonElement data) : A2AException(A2AErrorCode.InternalError, message, data);

    [LoggerMessage(EventId = LogEventIds.UnhandledTransportError, EventName = nameof(UnhandledTransportError), Level = LogLevel.Error,
        Message = "Unhandled error in A2A request. Trace id {TraceId}.")]
    private static partial void UnhandledTransportError(ILogger logger, Exception exception, string traceId);
}
