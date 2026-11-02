using System.Text;
using System.Text.Json;
using A2A.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace A2A.Server.Transports;

/// <summary>Exception → A2A error translation and shared HTTP error bodies. Used by every transport.</summary>
internal static class TransportErrors
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
                   ?? throw new A2AException(onError, "The request payload must be a JSON object.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException or InvalidOperationException)
        {
            throw new A2AException(onError, $"Invalid params: {ex.Message}");
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
        await using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteStartObject("error");
            w.WriteNumber("code", httpStatus);
            w.WriteString("status", status);
            w.WriteString("message", message);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        await http.Response.Body.WriteAsync(buffer.ToArray(), http.RequestAborted);
    }

    /// <summary>HTTP 404 for an agent name not in the catalog.</summary>
    public static Task WriteUnknownAgentAsync(HttpContext http, string? agentName)
        => WriteStatusAsync(http, StatusCodes.Status404NotFound, "NOT_FOUND", $"Unknown agent '{agentName}'.");

    private static A2AException Internal(Exception ex, ILogger logger)
    {
        logger.LogError(ex, "Unhandled error in A2A request.");
        return new A2AException(A2AErrorCode.InternalError, "Internal error.");
    }
}
