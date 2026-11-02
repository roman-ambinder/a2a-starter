using System.Net;
using System.Text.Json;
using A2A.Client.Auth;
using A2A.Protocol;

namespace A2A.Client.Transports;

/// <summary>Request headers, HTTP error mapping and response-shape validation shared by the HTTP-based client transports.</summary>
internal static class ClientSupport
{
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

    public static A2AException InvalidResponse(string message, Exception? inner = null)
        => inner is null
            ? new A2AException(A2AErrorCode.InvalidAgentResponse, message)
            : new A2AException(A2AErrorCode.InvalidAgentResponse, $"{message} {inner.Message}");

    /// <summary>Maps a wire error code to <see cref="A2AErrorCode"/>; unknown numbers become InternalError.</summary>
    public static A2AException ToException(int code, string? message, JsonElement? data)
    {
        message ??= "";
        if (Enum.IsDefined(typeof(A2AErrorCode), code))
            return new A2AException((A2AErrorCode)code, message, data);
        return new A2AException(A2AErrorCode.InternalError, $"{message} (server error code {code})", data);
    }

    /// <summary>ErrorInfo <c>metadata</c> (string map) as a JSON object, or null when empty.</summary>
    public static JsonElement? MetadataToData(IEnumerable<KeyValuePair<string, string>> metadata)
    {
        var pairs = metadata.ToList();
        return pairs.Count == 0 ? null : JsonSerializer.SerializeToElement(pairs.ToDictionary(p => p.Key, p => p.Value));
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
