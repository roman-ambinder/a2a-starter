using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace A2A.Server.Transports;

/// <summary>Reads a request body with a size cap.</summary>
internal static class RequestBody
{
    /// <summary>
    /// Returns the whole body, or null when it is larger than <paramref name="maxBytes"/> (the caller answers HTTP 413).
    /// Checks <c>Content-Length</c> first, lowers the server's own limit for this request, and counts bytes for chunked bodies.
    /// </summary>
    public static async Task<byte[]?> ReadAsync(HttpContext http, long maxBytes)
    {
        if (http.Request.ContentLength > maxBytes) return null;
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = maxBytes;

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        try
        {
            int read;
            while ((read = await http.Request.Body.ReadAsync(chunk, http.RequestAborted).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maxBytes) return null;
                buffer.Write(chunk, 0, read);
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return null;
        }
        return buffer.ToArray();
    }

    /// <summary>Answers HTTP 413 with no body.</summary>
    public static void WriteTooLarge(HttpContext http) => http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
}
