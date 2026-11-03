using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using A2A.Client.Transports.Sse;
using A2A.Protocol;

namespace A2A.Client.Transports;

/// <summary>
/// A2A client for the HTTP+JSON (REST) binding. A request with a tenant (its own, else the default tenant) uses the proto's
/// <c>/{tenant}/...</c> paths for every operation.
/// </summary>
public sealed class RestA2AClient : IA2AClient
{

    private readonly string _baseUrl;
    private readonly AgentCard _card;
    private readonly A2AClientOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string? _defaultTenant;
    private readonly string _protocolVersion;

    /// <param name="baseUrl">The REST base URL from the card's supportedInterfaces.</param>
    /// <param name="card">The agent card; handed to the credential provider.</param>
    /// <param name="defaultTenant">Tenant used when a request's tenant is empty (the card interface's <c>tenant</c>).</param>
    /// <param name="protocolVersion">The <c>Major.Minor</c> sent as <c>A2A-Version</c> (the selected card interface's version). Null → <see cref="A2AConstants.ProtocolVersion"/>.</param>
    public RestA2AClient(Uri baseUrl, AgentCard card, A2AClientOptions options, string? defaultTenant = null,
        string? protocolVersion = null)
    {
        _protocolVersion = string.IsNullOrWhiteSpace(protocolVersion) ? A2AConstants.ProtocolVersion : protocolVersion;
        _defaultTenant = string.IsNullOrEmpty(defaultTenant) ? null : defaultTenant;
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
    {
        request = ClientSupport.WithTenant(request, _defaultTenant);
        return ClientSupport.Validate(await CallAsync<SendMessageResponse>(HttpMethod.Post, Prefix(request.Tenant) + "/message:send", request,
            "SendMessage result", ct).ConfigureAwait(false));
    }

    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken ct = default)
    {
        request = ClientSupport.WithTenant(request, _defaultTenant);
        return StreamAsync(HttpMethod.Post, Prefix(request.Tenant) + "/message:stream", request, ct);
    }

    public Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken ct = default)
        => CallAsync<AgentTask>(HttpMethod.Get, $"{Prefix(request.Tenant)}/tasks/{Seg(request.Id)}{Query(request, "historyLength")}", null, "GetTask result", ct);

    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken ct = default)
        => CallAsync<ListTasksResponse>(HttpMethod.Get,
            Prefix(request.Tenant) + "/tasks" + Query(request, "contextId", "status", "pageSize", "pageToken", "historyLength", "statusTimestampAfter", "includeArtifacts"),
            null, "ListTasks result", ct);

    public Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken ct = default)
    {
        request = ClientSupport.WithTenant(request, _defaultTenant);
        return CallAsync<AgentTask>(HttpMethod.Post, $"{Prefix(request.Tenant)}/tasks/{Seg(request.Id)}:cancel", request, "CancelTask result", ct);
    }

    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken ct = default)
        => StreamAsync(HttpMethod.Get, $"{Prefix(request.Tenant)}/tasks/{Seg(request.Id)}:subscribe", null, ct);

    public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(TaskPushNotificationConfig request, CancellationToken ct = default)
    {
        request = ClientSupport.WithTenant(request, _defaultTenant);
        return CallAsync<TaskPushNotificationConfig>(HttpMethod.Post, $"{Prefix(request.Tenant)}/tasks/{Seg(request.TaskId)}/pushNotificationConfigs", request,
            "CreateTaskPushNotificationConfig result", ct);
    }

    public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken ct = default)
        => CallAsync<TaskPushNotificationConfig>(HttpMethod.Get,
            $"{Prefix(request.Tenant)}/tasks/{Seg(request.TaskId)}/pushNotificationConfigs/{Seg(request.Id)}", null,
            "GetTaskPushNotificationConfig result", ct);

    public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken ct = default)
        => CallAsync<ListTaskPushNotificationConfigsResponse>(HttpMethod.Get,
            $"{Prefix(request.Tenant)}/tasks/{Seg(request.TaskId)}/pushNotificationConfigs{Query(request, "pageSize", "pageToken")}", null,
            "ListTaskPushNotificationConfigs result", ct);

    public async Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Delete,
            $"{Prefix(request.Tenant)}/tasks/{Seg(request.TaskId)}/pushNotificationConfigs/{Seg(request.Id)}",
            null, A2AConstants.ContentType, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken ct = default)
        => CallAsync<AgentCard>(HttpMethod.Get, Prefix(request.Tenant) + "/extendedAgentCard", null, "GetExtendedAgentCard result", ct);

    public ValueTask DisposeAsync()
    {
        if (_ownsHttp) _http.Dispose();
        return ValueTask.CompletedTask;
    }

    // ---------- plumbing ----------

    private static string Seg(string? value) => Uri.EscapeDataString(value ?? "");

    /// <summary><c>/{tenant}</c> for the effective tenant (<see cref="ClientSupport.EffectiveTenant"/>); empty when there is none.</summary>
    private string Prefix(string? tenant)
    {
        var t = ClientSupport.EffectiveTenant(tenant, _defaultTenant);
        return t is null ? "" : "/" + Seg(t);
    }

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
        var final = false;
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
                throw ErrorFromStatus(error, null);

            var evt = ClientSupport.Validate(ClientSupport.Deserialize<StreamResponse>(root, "stream event"));
            final |= ClientSupport.IsFinal(evt);
            yield return evt;
        }
        if (!final) throw ClientSupport.StreamEndedEarly();
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
        await ClientSupport.ApplyHeadersAsync(message, _options, _card, _protocolVersion, accept, ct).ConfigureAwait(false);

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
    /// google.rpc.Status JSON → <see cref="A2AException"/>. The ErrorInfo <c>reason</c> selects the A2A code. Without one the
    /// HTTP status decides (the body's <c>code</c> for an in-stream frame): 400 → InvalidParams, 404 → MethodNotFound, anything
    /// else → InternalError. The message and the HTTP status are kept.
    /// </summary>
    private static A2AException ErrorFromStatus(JsonElement error, HttpResponseMessage? response)
    {
        var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;

        if (error.TryGetProperty("details", out var details) && ClientSupport.FindErrorInfo(details) is { } found)
            return new A2AException(found.Code, message ?? "", found.Metadata);

        // In-stream error frame: the HTTP status (200) says nothing, the body's code does.
        int? status = response is not null ? (int)response.StatusCode
            : error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n) ? n : null;
        var fallback = ClientSupport.CodeForStatusWithoutReason(status);
        if (response is null)
            return new A2AException(fallback, string.IsNullOrEmpty(message) ? "The agent reported an error." : message);
        return new A2AException(fallback, string.IsNullOrEmpty(message) ? $"The agent answered HTTP {status}." : $"{message} (HTTP {status})");
    }
}
