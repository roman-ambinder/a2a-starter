using System.Buffers;
using System.Text.Json;
using Ambinder.A2A.Protocol;
using Microsoft.AspNetCore.Http;

namespace Ambinder.A2A.Server.Transports.Rest;

/// <summary>Writes A2A errors for the HTTP+JSON binding in the google.rpc.Status JSON form.</summary>
public static class RestErrorWriter
{
    /// <summary>Sends the catalog's HTTP status and the body from <see cref="ToJson"/>. No-op body if the response has started.</summary>
    public static async Task Write(HttpContext http, A2AException error)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(error);
        if (http.Response.HasStarted) return;
        http.Response.StatusCode = A2AError.Get(error.Code).HttpStatus;
        http.Response.ContentType = A2AConstants.ContentType;
        await http.Response.Body.WriteAsync(ToJson(error), http.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>{"error":{"code":404,"status":"NOT_FOUND","message":"..","details":[ErrorInfo]}}</c>. ErrorInfo only for codes that
    /// have a reason; the exception's data (for example supported versions) becomes ErrorInfo <c>metadata</c>.
    /// </summary>
    public static byte[] ToJson(A2AException error)
    {
        var info = A2AError.Get(error.Code);
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteStartObject("error");
            w.WriteNumber("code", info.HttpStatus);
            w.WriteString("status", TransportErrors.StatusName(info.GrpcStatus));
            w.WriteString("message", error.Message);
            TransportErrors.WriteDetails(w, "details", error);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}
