using Ambinder.A2A.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ambinder.A2A.AspNetCore.Transports.Sse;

/// <summary>The SSE loop shared by JSON-RPC and REST. The transport supplies how a frame and an error frame look.</summary>
internal static class SseStreaming
{
    /// <summary>
    /// Waits for the first event before committing to SSE, so an error before it is thrown to the caller (which answers
    /// with a plain error response). Errors after that become one final error frame. Client disconnect ends quietly.
    /// </summary>
    public static async Task RunAsync(HttpContext http, IAsyncEnumerable<StreamResponse> events,
        Func<StreamResponse, byte[]> frame, Func<A2AException, byte[]> errorFrame, ILogger logger, CancellationToken ct)
    {
        var e = events.GetAsyncEnumerator(ct);
        await using (e.ConfigureAwait(false))
        {
            var hasFirst = await e.MoveNextAsync().ConfigureAwait(false);
            await SseWriter.StartAsync(http.Response, TransportRequest.GetResponseMetadata(http), ct).ConfigureAwait(false);
            if (!hasFirst) return;

            try
            {
                do
                {
                    await SseWriter.WriteEventAsync(http.Response, frame(e.Current), ct).ConfigureAwait(false);
                }
                while (await e.MoveNextAsync().ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Client disconnected: only this subscriber ends.
            }
            catch (Exception ex)
            {
                await SseWriter.WriteEventAsync(http.Response, errorFrame(TransportErrors.ToA2AException(ex, logger)), ct).ConfigureAwait(false);
            }
        }
    }
}
