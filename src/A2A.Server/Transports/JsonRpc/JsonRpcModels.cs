using System.Buffers;
using System.Text.Json;
using A2A.Protocol;

namespace A2A.Server.Transports.JsonRpc;

/// <summary>A validated JSON-RPC 2.0 request envelope.</summary>
/// <param name="Id">The request id (string, number or null), cloned. <see cref="JsonValueKind.Undefined"/> when absent; answered as <c>null</c>.</param>
/// <param name="Method">The method name (= gRPC rpc name).</param>
/// <param name="Params">The <c>params</c> member; <see cref="JsonValueKind.Undefined"/> when absent.</param>
internal readonly record struct JsonRpcRequest(JsonElement Id, string Method, JsonElement Params);

/// <summary>JSON-RPC 2.0 envelope parsing and response serialization.</summary>
internal static class JsonRpcMessages
{
    public const string Version = "2.0";

    /// <summary>
    /// Validates the envelope: a single object (batches are rejected), <c>jsonrpc</c> = "2.0", string <c>method</c>,
    /// <c>id</c> string/number/null/absent. On failure <paramref name="error"/> is InvalidRequest and
    /// <paramref name="id"/> is the id when it could be read.
    /// </summary>
    public static bool TryParse(JsonElement root, out JsonRpcRequest request, out JsonElement id, out A2AException? error)
    {
        request = default;
        id = default;
        error = null;
        if (root.ValueKind == JsonValueKind.Array)
        {
            error = new A2AException(A2AErrorCode.InvalidRequest, "Batch requests are not supported. Send one request per HTTP call.");
            return false;
        }
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = new A2AException(A2AErrorCode.InvalidRequest, "The request must be a JSON object. Send one JSON-RPC request object.");
            return false;
        }
        if (root.TryGetProperty("id", out var idElement))
        {
            if (idElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null))
            {
                error = new A2AException(A2AErrorCode.InvalidRequest, "id must be a string, a number or null. Send the id as one of those, not an object or an array.");
                return false;
            }
            id = idElement.Clone();
        }
        if (!root.TryGetProperty("jsonrpc", out var v) || v.ValueKind != JsonValueKind.String || v.GetString() != Version)
        {
            error = new A2AException(A2AErrorCode.InvalidRequest, "jsonrpc must be \"2.0\". Set \"jsonrpc\": \"2.0\".");
            return false;
        }
        if (!root.TryGetProperty("method", out var m) || m.ValueKind != JsonValueKind.String || m.GetString() is not { Length: > 0 } method)
        {
            error = new A2AException(A2AErrorCode.InvalidRequest, "method must be a non-empty string. Send the method name, for example SendMessage.");
            return false;
        }
        var parameters = root.TryGetProperty("params", out var p) ? p.Clone() : default;
        request = new JsonRpcRequest(id, method, parameters);
        return true;
    }

    /// <summary><c>{"jsonrpc":"2.0","id":..,"result":..}</c>. A null <paramref name="resultType"/> writes <c>"result":{}</c>.</summary>
    public static byte[] Result(JsonElement id, object? result, Type? resultType)
        => Write(id, w =>
        {
            w.WritePropertyName("result");
            if (resultType is null)
            {
                w.WriteStartObject();
                w.WriteEndObject();
            }
            else
            {
                JsonSerializer.Serialize(w, result, A2AJson.Options.GetTypeInfo(resultType));
            }
        });

    /// <summary>
    /// <c>{"jsonrpc":"2.0","id":..,"error":{"code":..,"message":..,"data":[ErrorInfo]}}</c> (spec 9.5): <c>data</c> is an array of
    /// <c>@type</c>-tagged details, present for A2A errors (those with an ErrorInfo reason), absent for standard JSON-RPC errors.
    /// </summary>
    public static byte[] Error(JsonElement id, A2AException error)
        => Write(id, w =>
        {
            w.WriteStartObject("error");
            w.WriteNumber("code", (int)error.Code);
            w.WriteString("message", error.Message);
            TransportErrors.WriteDetails(w, "data", error);
            w.WriteEndObject();
        });

    private static byte[] Write(JsonElement id, Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", Version);
            w.WritePropertyName("id");
            if (id.ValueKind == JsonValueKind.Undefined) w.WriteNullValue();
            else id.WriteTo(w);
            body(w);
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}
