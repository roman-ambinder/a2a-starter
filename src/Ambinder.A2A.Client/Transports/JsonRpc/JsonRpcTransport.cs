using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Sse;

namespace Ambinder.A2A.Client.Transports.JsonRpc;

/// <summary>
/// The JSON-RPC binding on the transport seam: <c>POST {endpoint}</c> with a JSON-RPC envelope, and SSE for the streaming methods.
/// It adds no interceptors, credentials, retries or timeouts; the client pipeline does. The tenant is already in the request.
/// </summary>
internal sealed class JsonRpcTransport : IA2ATransport
{
    private readonly Uri _endpoint;
    private readonly A2ATransportContext _context;
    private long _nextId;

    public JsonRpcTransport(Uri endpoint, A2ATransportContext context)
    {
        _endpoint = endpoint;
        _context = context;
    }

    /// <inheritdoc />
    public async ValueTask<A2ATransportResponse> SendAsync(A2ATransportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resultInfo = ResultTypeFor(request.Method);
        using var response = await PostAsync(request, A2AConstants.JsonRpcContentType, ct).ConfigureAwait(false);
        var info = NotifyHeaders(request, response);

        var success = response.IsSuccessStatusCode;
        using var buffer = await ReadBodyAsync(response, success, ct).ConfigureAwait(false);
        var value = ReadEnvelope(new ReadOnlySequence<byte>(buffer.Memory), resultInfo.TypeInfo, success, resultInfo.What, request.Method,
            (int)response.StatusCode);
        if (value is SendMessageResponse sent) value = ClientSupport.Validate(sent);
        return new A2ATransportResponse { Body = value ?? EmptyResult.Instance, Info = info };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamResponse> StreamAsync(A2ATransportRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var response = await PostAsync(request, A2AConstants.EventStream, ct).ConfigureAwait(false);
        NotifyHeaders(request, response);

        var isStream = response.IsSuccessStatusCode
            && string.Equals(response.Content.Headers.ContentType?.MediaType, A2AConstants.EventStream, StringComparison.OrdinalIgnoreCase);
        if (!isStream)
        {
            // Pre-stream error (or a protocol violation): a plain JSON-RPC response.
            var success = response.IsSuccessStatusCode;
            using var body = await ReadBodyAsync(response, success, ct).ConfigureAwait(false);
            ReadEnvelope(new ReadOnlySequence<byte>(body.Memory), null, success, "stream response", request.Method, (int)response.StatusCode);
            throw ClientSupport.InvalidResponse($"{request.Method} must answer with {A2AConstants.EventStream}.");
        }

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var pipe = PipeReader.Create(stream);
        var channel = Channel.CreateBounded<StreamResponse>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var producer = Task.Run(() => PumpAsync(pipe, channel.Writer, request.Method, cts.Token), CancellationToken.None);

        var final = false;
        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                final |= ClientSupport.IsFinal(evt);
                yield return evt;
            }
            if (!final) throw ClientSupport.StreamEndedEarly();
        }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The pump reports its failure through the channel; here only cleanup matters.
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask; // The HttpClient belongs to the client, not to this transport.

    // ---------- plumbing ----------

    private async Task PumpAsync(PipeReader pipe, ChannelWriter<StreamResponse> writer, string method, CancellationToken ct)
    {
        try
        {
            await SseParser.ReadAsync(pipe, _context.Limits.MaxEventBytes, _context.Timeouts.StreamIdleTimeout, _context.Time,
                async (data, token) =>
                {
                    if (IsBlank(data)) return; // heartbeat
                    var evt = ReadEvent(data, method);
                    await writer.WriteAsync(evt, token).ConfigureAwait(false);
                }, ct, _context.ClientName).ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
        finally
        {
            await pipe.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static bool IsBlank(in ReadOnlySequence<byte> data)
    {
        foreach (var segment in data)
        {
            foreach (var b in segment.Span)
            {
                if (b is not (0x20 or 0x09 or 0x0A or 0x0D)) return false;
            }
        }
        return true;
    }

    private StreamResponse ReadEvent(ReadOnlySequence<byte> data, string method)
    {
        var value = ReadEnvelope(data, A2AJson.StreamResponse, true, "stream event", method, 200);
        return ClientSupport.Validate((StreamResponse)value!);
    }

    private async Task<HttpResponseMessage> PostAsync(A2ATransportRequest request, string accept, CancellationToken ct)
    {
        var body = BuildBody(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new ReadOnlyMemoryContent(body.WrittenMemory),
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue(A2AConstants.JsonRpcContentType);
        message.Options.Set(A2ACallInfo.Key, request.CallInfo);
        AddHeader(message, A2AConstants.VersionHeader, request.ProtocolVersion);
        AddHeader(message, "Accept", accept);
        foreach (var (name, value) in request.Headers) AddHeader(message, name, value);
        if (request.Headers.AuthorizationForTransport is { } authorization) AddHeader(message, "Authorization", authorization);

        var response = await _context.HttpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        try
        {
            ThrowIfAuthFailure(response);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>Adds a header; a header HttpClient refuses throws <see cref="ArgumentException"/> naming it (never its value).</summary>
    private static void AddHeader(HttpRequestMessage message, string name, string value)
    {
        if (!message.Headers.TryAddWithoutValidation(name, value))
            throw new ArgumentException($"The header '{name}' cannot be added to the request.", nameof(name));
    }

    private static void ThrowIfAuthFailure(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)) return;
        var status = (int)response.StatusCode;
        var ex = new A2AAuthException(status, $"The agent refused the request (HTTP {status} {response.ReasonPhrase}).");
        if (response.Headers.TryGetValues("WWW-Authenticate", out var challenges))
            ex.Data["WWW-Authenticate"] = string.Join(", ", challenges);
        throw ex;
    }

    private static A2AResponseInfo NotifyHeaders(A2ATransportRequest request, HttpResponseMessage response)
    {
        var extensions = new List<string>();
        if (response.Headers.TryGetValues(A2AConstants.ExtensionsHeader, out var values))
        {
            foreach (var value in values)
            {
                foreach (var part in value.Split(','))
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length > 0) extensions.Add(trimmed);
                }
            }
        }
        var info = new A2AResponseInfo
        {
            ActivatedExtensions = extensions,
            Transport = A2AConstants.BindingJsonRpc,
            ProtocolVersion = request.ProtocolVersion,
            HttpStatus = (int)response.StatusCode,
            Attempts = 1,
        };
        request.OnHeaders?.Invoke(info);
        return info;
    }

    private ValueTask<PooledBuffer> ReadBodyAsync(HttpResponseMessage response, bool success, CancellationToken ct)
        => success
            ? BoundedContent.ReadAsync(response.Content, _context.Limits.MaxResponseBytes, "Response body", _context.ClientName,
                nameof(A2AClientLimits.MaxResponseBytes), ct)
            : BoundedContent.ReadAsync(response.Content, _context.Limits.MaxErrorBodyBytes, "Error body", _context.ClientName,
                nameof(A2AClientLimits.MaxErrorBodyBytes), ct);

    private ArrayBufferWriter<byte> BuildBody(A2ATransportRequest request)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using var w = new Utf8JsonWriter(buffer);
        w.WriteStartObject();
        w.WriteString("jsonrpc", "2.0");
        w.WriteNumber("id", Interlocked.Increment(ref _nextId));
        w.WriteString("method", request.Method);
        w.WritePropertyName("params");
        WriteParams(w, request.Body);
        w.WriteEndObject();
        w.Flush();
        return buffer;
    }

    private static void WriteParams(Utf8JsonWriter w, object body)
    {
        switch (body)
        {
            case SendMessageRequest r: JsonSerializer.Serialize(w, r, A2AJson.SendMessageRequest); break;
            case GetTaskRequest r: JsonSerializer.Serialize(w, r, A2AJson.GetTaskRequest); break;
            case ListTasksRequest r: JsonSerializer.Serialize(w, r, A2AJson.ListTasksRequest); break;
            case CancelTaskRequest r: JsonSerializer.Serialize(w, r, A2AJson.CancelTaskRequest); break;
            case SubscribeToTaskRequest r: JsonSerializer.Serialize(w, r, A2AJson.SubscribeToTaskRequest); break;
            case TaskPushNotificationConfig r: JsonSerializer.Serialize(w, r, A2AJson.TaskPushNotificationConfig); break;
            case GetTaskPushNotificationConfigRequest r: JsonSerializer.Serialize(w, r, A2AJson.GetTaskPushNotificationConfigRequest); break;
            case ListTaskPushNotificationConfigsRequest r: JsonSerializer.Serialize(w, r, A2AJson.ListTaskPushNotificationConfigsRequest); break;
            case DeleteTaskPushNotificationConfigRequest r: JsonSerializer.Serialize(w, r, A2AJson.DeleteTaskPushNotificationConfigRequest); break;
            case GetExtendedAgentCardRequest r: JsonSerializer.Serialize(w, r, A2AJson.GetExtendedAgentCardRequest); break;
            default: throw new ArgumentException($"The request body type '{body.GetType().Name}' is not an A2A request.", nameof(body));
        }
    }

    private static (JsonTypeInfo? TypeInfo, string What) ResultTypeFor(string method) => method switch
    {
        "SendMessage" => (A2AJson.SendMessageResponse, "SendMessage result"),
        "GetTask" => (A2AJson.AgentTask, "GetTask result"),
        "ListTasks" => (A2AJson.ListTasksResponse, "ListTasks result"),
        "CancelTask" => (A2AJson.AgentTask, "CancelTask result"),
        "CreateTaskPushNotificationConfig" => (A2AJson.TaskPushNotificationConfig, "CreateTaskPushNotificationConfig result"),
        "GetTaskPushNotificationConfig" => (A2AJson.TaskPushNotificationConfig, "GetTaskPushNotificationConfig result"),
        "ListTaskPushNotificationConfigs" => (A2AJson.ListTaskPushNotificationConfigsResponse, "ListTaskPushNotificationConfigs result"),
        "DeleteTaskPushNotificationConfig" => (null, "DeleteTaskPushNotificationConfig result"),
        "GetExtendedAgentCard" => (A2AJson.AgentCard, "GetExtendedAgentCard result"),
        _ => throw new ArgumentException($"'{method}' is not a unary A2A method.", nameof(method)),
    };

    /// <summary>
    /// Parses one JSON-RPC response (a unary body or one SSE frame) straight from the bytes. An <c>error</c> member throws. Otherwise
    /// the <c>result</c> is read with <paramref name="typeInfo"/> (skipped when it is <see langword="null"/>). A non-2xx status without
    /// a JSON-RPC error throws <see cref="A2ATransportException"/>.
    /// </summary>
    private object? ReadEnvelope(ReadOnlySequence<byte> json, JsonTypeInfo? typeInfo, bool success, string what, string method, int status)
    {
        var parsed = TryParse(json, typeInfo, success, what, out var value, out var hasResult, out var error);
        if (parsed == Parse.NotObject)
        {
            throw success ? ClientSupport.InvalidResponse("The response is not a JSON-RPC object.") : HttpFailure(status, method);
        }
        if (error is { } e) throw ClientSupport.ToException(e.Code, e.Message, e.Data);
        if (!success) throw HttpFailure(status, method);
        if (!hasResult) throw ClientSupport.InvalidResponse("The JSON-RPC response has neither result nor error.");
        if (typeInfo is not null && value is null) throw ClientSupport.InvalidResponse($"The {what} is null.");
        return value;
    }

    private A2ATransportException HttpFailure(int status, string method) => A2ATransportException.FromHttpStatus(status, _context.ClientName, method);

    private enum Parse { Ok, NotObject }

    private static Parse TryParse(ReadOnlySequence<byte> json, JsonTypeInfo? typeInfo, bool success, string what, out object? value,
        out bool hasResult, out (int Code, string? Message, JsonElement? Data)? error)
    {
        value = null;
        hasResult = false;
        error = null;
        var inResult = false;
        try
        {
            var reader = new Utf8JsonReader(json);
            if (!reader.Read()) throw new JsonException("The body is empty.");
            if (reader.TokenType != JsonTokenType.StartObject) return Parse.NotObject;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("result"u8))
                {
                    reader.Read();
                    hasResult = true;
                    if (success && typeInfo is not null)
                    {
                        inResult = true;
                        value = JsonSerializer.Deserialize(ref reader, typeInfo);
                        inResult = false;
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                else if (reader.ValueTextEquals("error"u8))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.StartObject)
                        error = ReadError(JsonElement.ParseValue(ref reader));
                    else
                        reader.Skip();
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }
            if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException("The JSON object is incomplete.");
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or A2AException))
        {
            if (!success && !inResult) return Parse.NotObject; // a non-2xx whose body is not JSON-RPC
            throw inResult
                ? ClientSupport.InvalidResponse($"The {what} is not valid.", ex)
                : ClientSupport.InvalidResponse("The response is not valid JSON.", ex);
        }
        return Parse.Ok;
    }

    private static (int Code, string? Message, JsonElement? Data) ReadError(JsonElement error)
    {
        var code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n)
            ? n
            : (int)A2AErrorCode.InternalError;
        var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        JsonElement? data = error.TryGetProperty("data", out var d) ? d : null;
        return (code, message, data);
    }

    /// <summary>The body of a response whose method has no result value (<c>DeleteTaskPushNotificationConfig</c>).</summary>
    internal sealed class EmptyResult
    {
        public static EmptyResult Instance { get; } = new();

        private EmptyResult()
        {
        }
    }
}
