using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using A2A.Client.Transports.Sse;
using A2A.Protocol;

namespace A2A.Client.Transports;

/// <summary>A2A client for the HTTP+JSON (REST) binding.</summary>
public sealed class RestA2AClient : IA2AClient
{
    private const string ErrorInfoType = "type.googleapis.com/google.rpc.ErrorInfo";

    private readonly string _baseUrl;
    private readonly AgentCard _card;
    private readonly A2AClientOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    /// <param name="baseUrl">The REST base URL from the card's supportedInterfaces.</param>
    /// <param name="card">The agent card; handed to the credential provider.</param>
    public RestA2AClient(Uri baseUrl, AgentCard card, A2AClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        _baseUrl = baseUrl.AbsoluteUri.TrimEnd('/');
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
        => ClientSupport.Validate(await CallAsync<SendMessageResponse>(HttpMethod.Post, "/message:send", request, "SendMessage result", ct).ConfigureAwait(false));

    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken ct = default)
        => StreamAsync(HttpMethod.Post, "/message:stream", request, ct);

    public Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken ct = default)
        => CallAsync<AgentTask>(HttpMethod.Get, $"/tasks/{Seg(request.Id)}{Query(request, "historyLength")}", null, "GetTask result", ct);

    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken ct = default)
        => CallAsync<ListTasksResponse>(HttpMethod.Get,
            "/tasks" + Query(request, "contextId", "status", "pageSize", "pageToken", "historyLength", "statusTimestampAfter", "includeArtifacts"),
            null, "ListTasks result", ct);

    public Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken ct = default)
        => CallAsync<AgentTask>(HttpMethod.Post, $"/tasks/{Seg(request.Id)}:cancel", request, "CancelTask result", ct);

    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken ct = default)
        => StreamAsync(HttpMethod.Get, $"/tasks/{Seg(request.Id)}:subscribe", null, ct);

    public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(TaskPushNotificationConfig request, CancellationToken ct = default)
        => CallAsync<TaskPushNotificationConfig>(HttpMethod.Post, $"/tasks/{Seg(request.TaskId)}/pushNotificationConfigs", request,
            "CreateTaskPushNotificationConfig result", ct);

    public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken ct = default)
        => CallAsync<TaskPushNotificationConfig>(HttpMethod.Get, $"/tasks/{Seg(request.TaskId)}/pushNotificationConfigs/{Seg(request.Id)}", null,
            "GetTaskPushNotificationConfig result", ct);

    public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken ct = default)
        => CallAsync<ListTaskPushNotificationConfigsResponse>(HttpMethod.Get,
            $"/tasks/{Seg(request.TaskId)}/pushNotificationConfigs{Query(request, "pageSize", "pageToken")}", null,
            "ListTaskPushNotificationConfigs result", ct);

    public async Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"/tasks/{Seg(request.TaskId)}/pushNotificationConfigs/{Seg(request.Id)}",
            null, A2AConstants.ContentType, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken ct = default)
        => CallAsync<AgentCard>(HttpMethod.Get, "/extendedAgentCard", null, "GetExtendedAgentCard result", ct);

    public ValueTask DisposeAsync()
    {
        if (_ownsHttp) _http.Dispose();
        return ValueTask.CompletedTask;
    }

    // ---------- plumbing ----------

    private static string Seg(string? value) => Uri.EscapeDataString(value ?? "");

    /// <summary>Builds <c>?name=value&amp;..</c> from the listed (camelCase) members of the serialized request; unset members are skipped.</summary>
    private static string Query<TRequest>(TRequest request, params string[] names)
    {
        var json = JsonSerializer.SerializeToElement(request, (JsonTypeInfo<TRequest>)A2AJson.Options.GetTypeInfo(typeof(TRequest)));
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

    private async Task<T> CallAsync<T>(HttpMethod method, string path, object? body, string what, CancellationToken ct)
    {
        using var response = await SendAsync(method, path, body, A2AConstants.ContentType, ct).ConfigureAwait(false);
        var bytes = await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        JsonElement element;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            element = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw ClientSupport.InvalidResponse($"The {what} is not valid JSON.", ex);
        }
        return ClientSupport.Deserialize<T>(element, what);
    }

    private async IAsyncEnumerable<StreamResponse> StreamAsync(HttpMethod method, string path, object? body,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await SendAsync(method, path, body, A2AConstants.EventStream, ct).ConfigureAwait(false);

        var isStream = response.IsSuccessStatusCode
            && string.Equals(response.Content.Headers.ContentType?.MediaType, A2AConstants.EventStream, StringComparison.OrdinalIgnoreCase);
        if (!isStream)
        {
            // Pre-stream error (plain JSON) or a protocol violation.
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            throw ClientSupport.InvalidResponse($"{path} must answer with {A2AConstants.EventStream}.");
        }

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await foreach (var data in SseReader.ReadAsync(stream, ct).ConfigureAwait(false))
        {
            if (!ClientSupport.IsPayload(data)) continue; // heartbeat
            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(data);
                root = doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw ClientSupport.InvalidResponse("A stream event is not valid JSON.", ex);
            }

            // After the stream started, the server reports an error as one final `{"error":{..}}` frame.
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                throw ErrorFromStatus(error, response);

            yield return ClientSupport.Validate(ClientSupport.Deserialize<StreamResponse>(root, "stream event"));
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string accept, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, new Uri(_baseUrl + path));
        if (body is not null)
        {
            message.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, A2AJson.Options.GetTypeInfo(body.GetType())));
            message.Content.Headers.ContentType = new MediaTypeHeaderValue(A2AConstants.ContentType);
        }
        else if (method == HttpMethod.Post)
        {
            message.Content = new ByteArrayContent([]);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue(A2AConstants.ContentType);
        }
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

    /// <summary>Returns the body of a 2xx response; a non-2xx response throws the exception its google.rpc.Status body describes.</summary>
    private static async Task<byte[]> EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) return bytes;

        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                throw ErrorFromStatus(error, response);
        }
        catch (JsonException)
        {
            // fall through: not an A2A error body
        }
        throw ClientSupport.HttpFailure(response);
    }

    /// <summary>
    /// google.rpc.Status JSON → <see cref="A2AException"/>. The ErrorInfo <c>reason</c> selects the A2A code; without one
    /// the error is InternalError, keeping the message and the HTTP status.
    /// </summary>
    private static A2AException ErrorFromStatus(JsonElement error, HttpResponseMessage response)
    {
        var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;

        if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
        {
            foreach (var detail in details.EnumerateArray())
            {
                if (detail.ValueKind != JsonValueKind.Object) continue;
                if (!detail.TryGetProperty("@type", out var type) || type.GetString() != ErrorInfoType) continue;
                if (!detail.TryGetProperty("reason", out var r) || r.ValueKind != JsonValueKind.String) continue;
                if (A2AError.FromReason(r.GetString()!) is not { } code) continue;

                JsonElement? data = null;
                if (detail.TryGetProperty("metadata", out var md) && md.ValueKind == JsonValueKind.Object)
                    data = ClientSupport.MetadataToData(md.EnumerateObject()
                        .Where(p => p.Value.ValueKind == JsonValueKind.String)
                        .Select(p => KeyValuePair.Create(p.Name, p.Value.GetString()!)));
                return new A2AException(code, message ?? "", data);
            }
        }

        var status = (int)response.StatusCode;
        return new A2AException(A2AErrorCode.InternalError, string.IsNullOrEmpty(message) ? $"The agent answered HTTP {status}." : $"{message} (HTTP {status})");
    }
}
