using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Ambinder.A2A.AspNetCore.Transports.Sse;

/// <summary>Writes Server-Sent Events frames (<c>data: &lt;json&gt;\n\n</c>), flushing after each one. Shared by JSON-RPC and REST.</summary>
internal static class SseWriter
{
    private static readonly byte[] Prefix = "data: "u8.ToArray();
    private static readonly byte[] Suffix = "\n\n"u8.ToArray();

    /// <summary>
    /// Sets status 200, <c>Content-Type: text/event-stream</c>, <c>Cache-Control: no-cache</c>, adds the allowed response metadata as headers,
    /// turns off buffering and sends the headers. Then the metadata is committed.
    /// </summary>
    public static async Task StartAsync(HttpResponse response, A2AResponseMetadata? metadata, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(response);
        TransportRequest.WriteResponseMetadata(response.HttpContext, metadata);
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = A2AConstants.EventStream;
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Writes one frame. <paramref name="utf8Json"/> must be single-line JSON (the default, non-indented serializer output).</summary>
    public static async Task WriteEventAsync(HttpResponse response, ReadOnlyMemory<byte> utf8Json, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(response);
        await response.Body.WriteAsync(Prefix, ct).ConfigureAwait(false);
        await response.Body.WriteAsync(utf8Json, ct).ConfigureAwait(false);
        await response.Body.WriteAsync(Suffix, ct).ConfigureAwait(false);
        await response.Body.FlushAsync(ct).ConfigureAwait(false);
    }
}
