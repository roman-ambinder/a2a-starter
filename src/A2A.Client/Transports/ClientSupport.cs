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
        request.Headers.TryAddWithoutValidation(A2AConstants.VersionHeader, options.ProtocolVersion);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        if (options.Extensions.Count > 0)
            request.Headers.TryAddWithoutValidation(A2AConstants.ExtensionsHeader, string.Join(',', options.Extensions));

        if (options.Credentials is { } credentials)
        {
            var headers = await credentials.GetHeadersAsync(card, ct).ConfigureAwait(false);
            foreach (var (name, value) in headers)
                request.Headers.TryAddWithoutValidation(name, value);
        }
    }

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

    /// <summary>Deserializes with the protocol JSON options; failure → InvalidAgentResponse.</summary>
    public static T Deserialize<T>(JsonElement element, string what)
    {
        try
        {
            var value = element.Deserialize((System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)A2AJson.Options.GetTypeInfo(typeof(T)));
            return value ?? throw InvalidResponse($"The {what} is null.");
        }
        catch (JsonException ex)
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
