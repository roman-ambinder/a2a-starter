using System.Net;
using System.Text.Json;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client.Transports;

/// <summary>Request headers, HTTP error mapping and response-shape validation shared by the HTTP-based client transports.</summary>
internal static class ClientSupport
{
    /// <summary>
    /// Creates the <see cref="HttpClient"/> the client owns when the caller passes none. The only place in <c>src</c> that calls a
    /// <see cref="HttpClient"/> constructor.
    /// </summary>
    /// <param name="handler">The handler to use, or <see langword="null"/> for the default handler.</param>
    public static HttpClient CreateOwnedHttpClient(HttpMessageHandler? handler = null)
    {
        // Single documented exception to the HttpClient ban (spec 02 section 7). Package 10 replaces it with IHttpClientFactory.
#pragma warning disable RS0030
        return handler is null ? new HttpClient() : new HttpClient(handler);
#pragma warning restore RS0030
    }

    /// <summary>Adds A2A-Version, Accept, A2A-Extensions and the credential headers.</summary>
    public static async ValueTask ApplyHeadersAsync(HttpRequestMessage request, A2AClientOptions options, AgentCard card,
        string accept, CancellationToken ct)
    {
        AddHeader(request, A2AConstants.VersionHeader, options.ProtocolVersion);
        AddHeader(request, "Accept", accept);
        if (options.Extensions.Count > 0)
            AddHeader(request, A2AConstants.ExtensionsHeader, string.Join(',', options.Extensions));

        if (options.Credentials is { } credentials)
        {
            var headers = await credentials.GetHeadersAsync(card, ct).ConfigureAwait(false);
            foreach (var (name, value) in headers)
                AddHeader(request, name, value);
        }
    }

    /// <summary>Adds a header; a header HttpClient refuses throws <see cref="ArgumentException"/> naming it (never its value).</summary>
    private static void AddHeader(HttpRequestMessage request, string name, string value)
    {
        if (!request.Headers.TryAddWithoutValidation(name, value))
            throw new ArgumentException($"The header '{name}' cannot be added to the request.", nameof(name));
    }

    /// <summary>Handler for a client that creates its own HttpClient (mutual TLS certificate applied).</summary>
    public static SocketsHttpHandler CreateHandler(A2AClientOptions options)
    {
        var handler = new SocketsHttpHandler();
        if (options.ClientCertificate is { } cert)
            handler.SslOptions.ClientCertificates = [cert];
        return handler;
    }

    /// <summary>
    /// <paramref name="request"/> with <c>Tenant</c> set to <paramref name="defaultTenant"/> (the selected interface's tenant), as
    /// spec 8.3.2 requires. A request whose tenant is set and differs throws <see cref="ArgumentException"/>. Without an interface
    /// tenant the request is returned unchanged.
    /// </summary>
    public static T WithTenant<T>(T request, string? defaultTenant)
    {
        if (string.IsNullOrEmpty(defaultTenant)) return request;
        var current = request switch
        {
            SendMessageRequest r => r.Tenant,
            GetTaskRequest r => r.Tenant,
            ListTasksRequest r => r.Tenant,
            CancelTaskRequest r => r.Tenant,
            SubscribeToTaskRequest r => r.Tenant,
            TaskPushNotificationConfig r => r.Tenant,
            GetTaskPushNotificationConfigRequest r => r.Tenant,
            ListTaskPushNotificationConfigsRequest r => r.Tenant,
            DeleteTaskPushNotificationConfigRequest r => r.Tenant,
            GetExtendedAgentCardRequest r => r.Tenant,
            _ => null,
        };
        if (EffectiveTenant(current, defaultTenant) == current) return request;
        object? result = request switch
        {
            SendMessageRequest r => r with { Tenant = defaultTenant },
            GetTaskRequest r => r with { Tenant = defaultTenant },
            ListTasksRequest r => r with { Tenant = defaultTenant },
            CancelTaskRequest r => r with { Tenant = defaultTenant },
            SubscribeToTaskRequest r => r with { Tenant = defaultTenant },
            TaskPushNotificationConfig r => r with { Tenant = defaultTenant },
            GetTaskPushNotificationConfigRequest r => r with { Tenant = defaultTenant },
            ListTaskPushNotificationConfigsRequest r => r with { Tenant = defaultTenant },
            DeleteTaskPushNotificationConfigRequest r => r with { Tenant = defaultTenant },
            GetExtendedAgentCardRequest r => r with { Tenant = defaultTenant },
            _ => request,
        };
        return (T)result!;
    }

    /// <summary>
    /// The tenant to send: the interface tenant when there is one (a different non-empty request tenant throws
    /// <see cref="ArgumentException"/>), else the request tenant.
    /// </summary>
    public static string? EffectiveTenant(string? requestTenant, string? interfaceTenant)
    {
        if (string.IsNullOrEmpty(interfaceTenant)) return string.IsNullOrEmpty(requestTenant) ? null : requestTenant;
        if (string.IsNullOrEmpty(requestTenant) || requestTenant == interfaceTenant) return interfaceTenant;
#pragma warning disable CA2208 // "request" is the caller's argument; the parameter name is part of the message that callers see.
        throw new ArgumentException(
            $"This client talks to tenant '{interfaceTenant}' (from the agent card); the request asks for tenant '{requestTenant}'. " +
            "Leave the request tenant empty, or create a client for the other interface.", "request");
#pragma warning restore CA2208
    }

    /// <summary>True for an SSE payload with content; blank payloads are heartbeats.</summary>
    public static bool IsPayload(string data) => !string.IsNullOrWhiteSpace(data);

    /// <summary>HTTP 401/403 → <see cref="A2AAuthException"/>. Does nothing for other statuses.</summary>
    public static void ThrowIfAuthFailure(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new A2AAuthException((int)response.StatusCode,
                $"The agent refused the request (HTTP {(int)response.StatusCode} {response.ReasonPhrase}).");
    }

    /// <summary>The exception for a non-2xx response that carries no A2A error body.</summary>
    public static A2AException HttpFailure(HttpResponseMessage response)
        => new(A2AErrorCode.InternalError, $"The agent answered HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");

    /// <summary>
    /// The A2A code for an HTTP error that has no ErrorInfo reason: 400 → InvalidParams, 404 → MethodNotFound, else InternalError.
    /// Same rule as <c>GrpcErrorTranslator</c> for INVALID_ARGUMENT / UNIMPLEMENTED.
    /// </summary>
    public static A2AErrorCode CodeForStatusWithoutReason(int? httpStatus) => httpStatus switch
    {
        400 => A2AErrorCode.InvalidParams,
        404 => A2AErrorCode.MethodNotFound,
        _ => A2AErrorCode.InternalError,
    };

    public static A2AException InvalidResponse(string message, Exception? inner = null)
        => inner is null
            ? new A2AException(A2AErrorCode.InvalidAgentResponse, message)
            : new A2AException(A2AErrorCode.InvalidAgentResponse, $"{message} {inner.Message}");

    private const string ErrorInfoType = "type.googleapis.com/google.rpc.ErrorInfo";

    /// <summary>
    /// Maps a JSON-RPC error to <see cref="A2AException"/>. A google.rpc.ErrorInfo in the <c>data</c> array (spec 9.5) wins: its
    /// reason selects the code and its metadata becomes <see cref="A2AException.Data"/>. Otherwise the numeric code is used (unknown
    /// numbers become InternalError) and a <c>data</c> value that is not a detail array is kept as is.
    /// </summary>
    public static A2AException ToException(int code, string? message, JsonElement? data)
    {
        message ??= "";
        if (data is { } d && FindErrorInfo(d) is { } found)
            return new A2AException(found.Code, message, found.Metadata);
        var raw = data is { ValueKind: JsonValueKind.Array } ? null : data;
        if (Enum.IsDefined(typeof(A2AErrorCode), code))
            return new A2AException((A2AErrorCode)code, message, raw);
        return new A2AException(A2AErrorCode.InternalError, $"{message} (server error code {code})", raw);
    }

    /// <summary>
    /// The first google.rpc.ErrorInfo detail in an array of ProtoJSON <c>Any</c> objects whose reason is in the A2A catalog,
    /// with its string metadata as a JSON object (null when empty). Null when there is none.
    /// </summary>
    public static (A2AErrorCode Code, JsonElement? Metadata)? FindErrorInfo(JsonElement details)
    {
        if (details.ValueKind != JsonValueKind.Array) return null;
        foreach (var detail in details.EnumerateArray())
        {
            if (detail.ValueKind != JsonValueKind.Object) continue;
            if (!detail.TryGetProperty("@type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != ErrorInfoType) continue;
            if (!detail.TryGetProperty("reason", out var r) || r.ValueKind != JsonValueKind.String) continue;
            if (A2AError.FromReason(r.GetString()!) is not { } code) continue;

            JsonElement? metadata = null;
            if (detail.TryGetProperty("metadata", out var md) && md.ValueKind == JsonValueKind.Object)
                metadata = MetadataToData(md.EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.String)
                    .Select(p => KeyValuePair.Create(p.Name, p.Value.GetString()!)));
            return (code, metadata);
        }
        return null;
    }

    /// <summary>
    /// ErrorInfo <c>metadata</c> (string map) as a JSON object, or null when empty. ErrorInfo metadata is string-only, so
    /// <see cref="A2AException.Data"/> is a string map on every transport.
    /// </summary>
    public static JsonElement? MetadataToData(IEnumerable<KeyValuePair<string, string>> metadata)
    {
        var map = metadata.ToDictionary(p => p.Key, p => p.Value);
        if (map.Count == 0) return null;
        // Written by hand: the reflection-based JsonSerializer overloads are not trim or AOT safe.
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in map) writer.WriteString(key, value);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>Deserializes with the protocol JSON options; failure → InvalidAgentResponse.</summary>
    public static T Deserialize<T>(JsonElement element, string what)
    {
        try
        {
            var value = element.Deserialize((System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)A2AJson.Options.GetTypeInfo(typeof(T)));
            return value ?? throw InvalidResponse($"The {what} is null.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not A2AException)
        {
            throw InvalidResponse($"The {what} is not valid.", ex);
        }
    }

    /// <summary>
    /// True for an event that may end a stream: a message, or a task / status update in a terminal (COMPLETED, FAILED, CANCELED,
    /// REJECTED) or interrupted (INPUT_REQUIRED, AUTH_REQUIRED) state.
    /// </summary>
    public static bool IsFinal(StreamResponse e)
        => e.Message is not null || IsFinalState(e.Task?.Status.State) || IsFinalState(e.StatusUpdate?.Status.State);

    private static bool IsFinalState(TaskState? state) => state is TaskState.Completed or TaskState.Failed or TaskState.Canceled
        or TaskState.Rejected or TaskState.InputRequired or TaskState.AuthRequired;

    /// <summary>The error for a stream that closed without a final event (see <see cref="IsFinal"/>).</summary>
    public static A2AException StreamEndedEarly() => InvalidResponse("stream ended before a final event");

    /// <summary>Exactly one of task / message.</summary>
    public static SendMessageResponse Validate(SendMessageResponse response)
        => (response.Task is not null) == (response.Message is not null)
            ? throw InvalidResponse("SendMessage result must contain exactly one of task or message.")
            : response;

    /// <summary>Exactly one of task / message / statusUpdate / artifactUpdate.</summary>
    public static StreamResponse Validate(StreamResponse response)
        => ((response.Task is not null ? 1 : 0) + (response.Message is not null ? 1 : 0)
            + (response.StatusUpdate is not null ? 1 : 0) + (response.ArtifactUpdate is not null ? 1 : 0)) != 1
            ? throw InvalidResponse("A stream event must contain exactly one payload.")
            : response;
}
