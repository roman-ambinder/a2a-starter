using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Sse;

namespace Ambinder.A2A.Client.Transports.Rest;

/// <summary>
/// The HTTP+JSON (REST) binding on the transport seam. A request with a tenant uses the proto's <c>/{tenant}/...</c> paths for
/// every operation. It adds no interceptors, credentials, retries or timeouts; the client pipeline does. The tenant is already in the request.
/// </summary>
internal sealed class RestTransport : IA2ATransport
{
    private readonly string _baseUrl;
    private readonly A2ATransportContext _context;

    public RestTransport(Uri baseUrl, A2ATransportContext context)
    {
        _baseUrl = baseUrl.AbsoluteUri.TrimEnd('/');
        _context = context;
    }

    /// <inheritdoc />
    public async ValueTask<A2ATransportResponse> SendAsync(A2ATransportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var route = UnaryRoute(request);
        using var response = await SendHttpAsync(request, route.Verb, route.Path, route.Body, A2AConstants.ContentType, ct).ConfigureAwait(false);
        var info = NotifyHeaders(request, response);

        var success = response.IsSuccessStatusCode;
        using var buffer = await ReadBodyAsync(response, success, ct).ConfigureAwait(false);
        if (!success) throw ErrorFor(new ReadOnlySequence<byte>(buffer.Memory), (int)response.StatusCode, request.Method);

        object value = route.Result is null
            ? EmptyResult.Instance
            : Deserialize(new ReadOnlySequence<byte>(buffer.Memory), route.Result, route.What);
        if (value is SendMessageResponse sent) value = ClientSupport.Validate(sent);
        return new A2ATransportResponse { Body = value, Info = info };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamResponse> StreamAsync(A2ATransportRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (verb, path, body) = StreamRoute(request);
        using var response = await SendHttpAsync(request, verb, path, body, A2AConstants.EventStream, ct).ConfigureAwait(false);
        NotifyHeaders(request, response);

        var success = response.IsSuccessStatusCode;
        var isStream = success
            && string.Equals(response.Content.Headers.ContentType?.MediaType, A2AConstants.EventStream, StringComparison.OrdinalIgnoreCase);
        if (!isStream)
        {
            // Pre-stream error (plain JSON) or a protocol violation.
            using var errorBody = await ReadBodyAsync(response, success, ct).ConfigureAwait(false);
            if (!success) throw ErrorFor(new ReadOnlySequence<byte>(errorBody.Memory), (int)response.StatusCode, request.Method);
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
        var producer = Task.Run(() => PumpAsync(pipe, channel.Writer, cts.Token), CancellationToken.None);

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

    // ---------- routes ----------

    private readonly record struct Route(HttpMethod Verb, string Path, ArrayBufferWriter<byte>? Body, JsonTypeInfo? Result, string What);

    private static Route UnaryRoute(A2ATransportRequest request)
    {
        var body = request.Body;
        switch (request.Method)
        {
            case "SendMessage" when body is SendMessageRequest r:
                return new Route(HttpMethod.Post, Prefix(r.Tenant) + "/message:send", Body(r), A2AJson.SendMessageResponse, "SendMessage result");
            case "GetTask" when body is GetTaskRequest r:
                return new Route(HttpMethod.Get, $"{Prefix(r.Tenant)}/tasks/{Seg(r.Id)}{Query(r, A2AJson.GetTaskRequest, "historyLength")}", null,
                    A2AJson.AgentTask, "GetTask result");
            case "ListTasks" when body is ListTasksRequest r:
                return new Route(HttpMethod.Get,
                    Prefix(r.Tenant) + "/tasks" + Query(r, A2AJson.ListTasksRequest, "contextId", "status", "pageSize", "pageToken", "historyLength",
                        "statusTimestampAfter", "includeArtifacts"),
                    null, A2AJson.ListTasksResponse, "ListTasks result");
            case "CancelTask" when body is CancelTaskRequest r:
                return new Route(HttpMethod.Post, $"{Prefix(r.Tenant)}/tasks/{Seg(r.Id)}:cancel", Body(r), A2AJson.AgentTask, "CancelTask result");
            case "CreateTaskPushNotificationConfig" when body is TaskPushNotificationConfig r:
                return new Route(HttpMethod.Post, $"{Prefix(r.Tenant)}/tasks/{Seg(r.TaskId)}/pushNotificationConfigs", Body(r),
                    A2AJson.TaskPushNotificationConfig, "CreateTaskPushNotificationConfig result");
            case "GetTaskPushNotificationConfig" when body is GetTaskPushNotificationConfigRequest r:
                return new Route(HttpMethod.Get, $"{Prefix(r.Tenant)}/tasks/{Seg(r.TaskId)}/pushNotificationConfigs/{Seg(r.Id)}", null,
                    A2AJson.TaskPushNotificationConfig, "GetTaskPushNotificationConfig result");
            case "ListTaskPushNotificationConfigs" when body is ListTaskPushNotificationConfigsRequest r:
                return new Route(HttpMethod.Get,
                    $"{Prefix(r.Tenant)}/tasks/{Seg(r.TaskId)}/pushNotificationConfigs{Query(r, A2AJson.ListTaskPushNotificationConfigsRequest, "pageSize", "pageToken")}",
                    null, A2AJson.ListTaskPushNotificationConfigsResponse, "ListTaskPushNotificationConfigs result");
            case "DeleteTaskPushNotificationConfig" when body is DeleteTaskPushNotificationConfigRequest r:
                return new Route(HttpMethod.Delete, $"{Prefix(r.Tenant)}/tasks/{Seg(r.TaskId)}/pushNotificationConfigs/{Seg(r.Id)}", null, null,
                    "DeleteTaskPushNotificationConfig result");
            case "GetExtendedAgentCard" when body is GetExtendedAgentCardRequest r:
                return new Route(HttpMethod.Get, Prefix(r.Tenant) + "/extendedAgentCard", null, A2AJson.AgentCard, "GetExtendedAgentCard result");
            default:
                throw new ArgumentException($"'{request.Method}' is not a unary A2A method for a '{request.Body.GetType().Name}' request.", nameof(request));
        }
    }

    private static (HttpMethod Verb, string Path, ArrayBufferWriter<byte>? Body) StreamRoute(A2ATransportRequest request)
    {
        switch (request.Method)
        {
            case "SendStreamingMessage" when request.Body is SendMessageRequest r:
                return (HttpMethod.Post, Prefix(r.Tenant) + "/message:stream", Body(r));
            case "SubscribeToTask" when request.Body is SubscribeToTaskRequest r:
                return (HttpMethod.Get, $"{Prefix(r.Tenant)}/tasks/{Seg(r.Id)}:subscribe", null);
            default:
                throw new ArgumentException($"'{request.Method}' is not a streaming A2A method for a '{request.Body.GetType().Name}' request.", nameof(request));
        }
    }

    private static string Seg(string? value) => Uri.EscapeDataString(value ?? "");

    /// <summary><c>/{tenant}</c> when the request has a tenant; empty otherwise.</summary>
    private static string Prefix(string? tenant) => string.IsNullOrEmpty(tenant) ? "" : "/" + Seg(tenant);

    private static ArrayBufferWriter<byte> Body(SendMessageRequest r) => Write(r, A2AJson.SendMessageRequest);

    private static ArrayBufferWriter<byte> Body(CancelTaskRequest r) => Write(r, A2AJson.CancelTaskRequest);

    private static ArrayBufferWriter<byte> Body(TaskPushNotificationConfig r) => Write(r, A2AJson.TaskPushNotificationConfig);

    private static ArrayBufferWriter<byte> Write<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using var writer = new Utf8JsonWriter(buffer);
        JsonSerializer.Serialize(writer, value, typeInfo);
        writer.Flush();
        return buffer;
    }

    /// <summary>Builds <c>?name=value&amp;..</c> from the listed (camelCase) members of the serialized request; unset members are skipped.</summary>
    private static string Query<T>(T request, JsonTypeInfo<T> typeInfo, params string[] names)
    {
        var json = JsonSerializer.SerializeToElement(request, typeInfo);
        var sb = new StringBuilder();
        foreach (var name in names)
        {
            if (!json.TryGetProperty(name, out var value)) continue;
            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };
            if (text is null) continue;
            sb.Append(sb.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(text));
        }
        return sb.ToString();
    }

    // ---------- plumbing ----------

    private async Task<HttpResponseMessage> SendHttpAsync(A2ATransportRequest request, HttpMethod verb, string path, ArrayBufferWriter<byte>? body,
        string accept, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(verb, new Uri(_baseUrl + path))
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        if (body is not null)
        {
            message.Content = new ReadOnlyMemoryContent(body.WrittenMemory);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue(A2AConstants.ContentType);
        }
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
            Transport = A2AConstants.BindingRest,
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

    private static object Deserialize(ReadOnlySequence<byte> json, JsonTypeInfo typeInfo, string what)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            return JsonSerializer.Deserialize(ref reader, typeInfo) ?? throw ClientSupport.InvalidResponse($"The {what} is null.");
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or A2AException))
        {
            throw ClientSupport.InvalidResponse($"The {what} is not valid.", ex);
        }
    }

    // ---------- streams ----------

    private async Task PumpAsync(PipeReader pipe, ChannelWriter<StreamResponse> writer, CancellationToken ct)
    {
        try
        {
            await SseParser.ReadAsync(pipe, _context.Limits.MaxEventBytes, _context.Timeouts.StreamIdleTimeout, _context.Time,
                async (data, token) =>
                {
                    if (IsBlank(data)) return; // heartbeat
                    await writer.WriteAsync(ReadEvent(data), token).ConfigureAwait(false);
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

    /// <summary>
    /// One SSE frame. After the stream started, the server reports an error as one final <c>{"error":{..}}</c> frame; a stream event
    /// has exactly one member, so the first property name tells the two apart without parsing twice.
    /// </summary>
    private StreamResponse ReadEvent(ReadOnlySequence<byte> data)
    {
        if (StartsWithError(data)) throw ErrorFor(data, null, "stream");
        return ClientSupport.Validate((StreamResponse)Deserialize(data, A2AJson.StreamResponse, "stream event"));
    }

    private static bool StartsWithError(ReadOnlySequence<byte> data)
    {
        try
        {
            var reader = new Utf8JsonReader(data);
            return reader.Read() && reader.TokenType == JsonTokenType.StartObject
                && reader.Read() && reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("error"u8);
        }
        catch (JsonException)
        {
            return false; // Deserialize reports the invalid JSON.
        }
    }

    // ---------- errors ----------

    /// <summary>
    /// The exception for an error body. A <c>{"error":{..}}</c> google.rpc.Status maps through its ErrorInfo reason, else by status
    /// (<paramref name="httpStatus"/>, or the body's <c>code</c> for an in-stream frame). A body that is not one gives an
    /// <see cref="A2ATransportException"/> for the HTTP status.
    /// </summary>
    private Exception ErrorFor(ReadOnlySequence<byte> json, int? httpStatus, string method)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                return ErrorFromStatus(error, httpStatus);
        }
        catch (JsonException)
        {
            // fall through: not an A2A error body
        }
        return httpStatus is { } status
            ? A2ATransportException.FromHttpStatus(status, _context.ClientName, method)
            : ClientSupport.InvalidResponse("The agent sent an invalid error frame.");
    }

    private static A2AException ErrorFromStatus(JsonElement error, int? httpStatus)
    {
        var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;

        if (error.TryGetProperty("details", out var details) && ClientSupport.FindErrorInfo(details) is { } found)
            return new A2AException(found.Code, message ?? "", found.Metadata);

        // In-stream error frame: there is no HTTP status to use, the body's code decides.
        var status = httpStatus
            ?? (error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n) ? n : (int?)null);
        var fallback = ClientSupport.CodeForStatusWithoutReason(status);
        var metadata = error.TryGetProperty("details", out var d) ? ClientSupport.FindMetadata(d) : null;
        if (httpStatus is null)
            return new A2AException(fallback, string.IsNullOrEmpty(message) ? "The agent reported an error." : message, metadata);
        return new A2AException(fallback, string.IsNullOrEmpty(message) ? $"The agent answered HTTP {status}." : $"{message} (HTTP {status})", metadata);
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
