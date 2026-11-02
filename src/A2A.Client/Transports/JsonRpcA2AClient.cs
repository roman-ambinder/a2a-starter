using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using A2A.Client.Transports.Sse;
using A2A.Protocol;

namespace A2A.Client.Transports;

/// <summary>A2A client for the JSON-RPC binding (<c>POST {endpoint}</c>, SSE for streaming methods).</summary>
public sealed class JsonRpcA2AClient : IA2AClient
{
    private readonly Uri _endpoint;
    private readonly AgentCard _card;
    private readonly A2AClientOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string? _defaultTenant;
    private long _nextId;

    /// <param name="endpoint">The JSON-RPC URL from the card's supportedInterfaces.</param>
    /// <param name="card">The agent card; handed to the credential provider.</param>
    /// <param name="defaultTenant">Tenant sent when a request's tenant is empty (the card interface's <c>tenant</c>).</param>
    public JsonRpcA2AClient(Uri endpoint, AgentCard card, A2AClientOptions options, string? defaultTenant = null)
    {
        _defaultTenant = string.IsNullOrEmpty(defaultTenant) ? null : defaultTenant;
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.HttpClient is { } http)
        {
            _http = http;
        }
        else
        {
            _http = new HttpClient(ClientSupport.CreateHandler(options));
            _ownsHttp = true;
        }
    }

    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken ct = default)
        => ClientSupport.Validate(ClientSupport.Deserialize<SendMessageResponse>(
            await CallAsync("SendMessage", request, ct).ConfigureAwait(false), "SendMessage result"));

    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken ct = default)
        => StreamAsync("SendStreamingMessage", request, ct);

    public async Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken ct = default)
        => ClientSupport.Deserialize<AgentTask>(await CallAsync("GetTask", request, ct).ConfigureAwait(false), "GetTask result");

    public async Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken ct = default)
        => ClientSupport.Deserialize<ListTasksResponse>(await CallAsync("ListTasks", request, ct).ConfigureAwait(false), "ListTasks result");

    public async Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken ct = default)
        => ClientSupport.Deserialize<AgentTask>(await CallAsync("CancelTask", request, ct).ConfigureAwait(false), "CancelTask result");

    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken ct = default)
        => StreamAsync("SubscribeToTask", request, ct);

    public async Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(TaskPushNotificationConfig request, CancellationToken ct = default)
        => ClientSupport.Deserialize<TaskPushNotificationConfig>(
            await CallAsync("CreateTaskPushNotificationConfig", request, ct).ConfigureAwait(false), "CreateTaskPushNotificationConfig result");

    public async Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken ct = default)
        => ClientSupport.Deserialize<TaskPushNotificationConfig>(
            await CallAsync("GetTaskPushNotificationConfig", request, ct).ConfigureAwait(false), "GetTaskPushNotificationConfig result");

    public async Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken ct = default)
        => ClientSupport.Deserialize<ListTaskPushNotificationConfigsResponse>(
            await CallAsync("ListTaskPushNotificationConfigs", request, ct).ConfigureAwait(false), "ListTaskPushNotificationConfigs result");

    public async Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken ct = default)
        => _ = await CallAsync("DeleteTaskPushNotificationConfig", request, ct).ConfigureAwait(false);

    public async Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken ct = default)
        => ClientSupport.Deserialize<AgentCard>(await CallAsync("GetExtendedAgentCard", request, ct).ConfigureAwait(false), "GetExtendedAgentCard result");

    public ValueTask DisposeAsync()
    {
        if (_ownsHttp) _http.Dispose();
        return ValueTask.CompletedTask;
    }

    // ---------- plumbing ----------

    private async Task<JsonElement> CallAsync<TRequest>(string method, TRequest request, CancellationToken ct)
    {
        using var response = await SendAsync(method, request, A2AConstants.ContentType, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return ReadEnvelope(response, body);
    }

    private async IAsyncEnumerable<StreamResponse> StreamAsync<TRequest>(string method, TRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await SendAsync(method, request, A2AConstants.EventStream, ct).ConfigureAwait(false);

        var isStream = response.IsSuccessStatusCode
            && string.Equals(response.Content.Headers.ContentType?.MediaType, A2AConstants.EventStream, StringComparison.OrdinalIgnoreCase);
        if (!isStream)
        {
            // Pre-stream error (or a protocol violation): a plain JSON-RPC response.
            var body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            ReadEnvelope(response, body); // throws for an error; a result here is not a stream
            throw ClientSupport.InvalidResponse($"{method} must answer with {A2AConstants.EventStream}.");
        }

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var final = false;
        await foreach (var data in SseReader.ReadAsync(stream, ct).ConfigureAwait(false))
        {
            if (!ClientSupport.IsPayload(data)) continue; // heartbeat
            var result = ReadEnvelope(response, System.Text.Encoding.UTF8.GetBytes(data));
            var evt = ClientSupport.Validate(ClientSupport.Deserialize<StreamResponse>(result, "stream event"));
            final |= ClientSupport.IsFinal(evt);
            yield return evt;
        }
        if (!final) throw ClientSupport.StreamEndedEarly();
    }

    private async Task<HttpResponseMessage> SendAsync<TRequest>(string method, TRequest request, string accept, CancellationToken ct)
    {
        request = ClientSupport.WithTenant(request, _defaultTenant);
        using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new ByteArrayContent(BuildBody(method, request)),
        };
        message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(A2AConstants.ContentType);
        await ClientSupport.ApplyHeadersAsync(message, _options, _card, accept, ct).ConfigureAwait(false);

        var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        try
        {
            ClientSupport.ThrowIfAuthFailure(response);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private byte[] BuildBody<TRequest>(string method, TRequest request)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WriteNumber("id", Interlocked.Increment(ref _nextId));
            w.WriteString("method", method);
            w.WritePropertyName("params");
            JsonSerializer.Serialize(w, request, (JsonTypeInfo<TRequest>)A2AJson.Options.GetTypeInfo(typeof(TRequest)));
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Parses one JSON-RPC response (a unary body or one SSE frame). An <c>error</c> member throws; otherwise
    /// returns <c>result</c>. A non-2xx status without a JSON-RPC body throws InternalError.
    /// </summary>
    private static JsonElement ReadEnvelope(HttpResponseMessage response, byte[] body)
    {
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            if (!response.IsSuccessStatusCode) throw ClientSupport.HttpFailure(response);
            throw ClientSupport.InvalidResponse("The response is not valid JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw response.IsSuccessStatusCode ? ClientSupport.InvalidResponse("The response is not a JSON-RPC object.") : ClientSupport.HttpFailure(response);

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n)
                    ? n
                    : (int)A2AErrorCode.InternalError;
                var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                JsonElement? data = error.TryGetProperty("data", out var d) ? d.Clone() : null;
                throw ClientSupport.ToException(code, message, data);
            }

            if (!response.IsSuccessStatusCode) throw ClientSupport.HttpFailure(response);
            if (!root.TryGetProperty("result", out var result))
                throw ClientSupport.InvalidResponse("The JSON-RPC response has neither result nor error.");
            return result.Clone();
        }
    }
}
