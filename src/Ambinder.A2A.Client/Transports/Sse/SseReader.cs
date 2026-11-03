using System.Runtime.CompilerServices;
using System.Text;

namespace Ambinder.A2A.Client.Transports.Sse;

/// <summary>Reads Server-Sent Events and yields the data payload of each event.</summary>
public static class SseReader
{
    /// <summary>
    /// Yields one string per event that has <c>data:</c> lines (multiple lines joined with <c>\n</c>).
    /// Comments and the <c>event</c>/<c>id</c>/<c>retry</c> fields are ignored. Handles CRLF, LF and CR,
    /// and a last event without a closing blank line.
    /// </summary>
    public static async IAsyncEnumerable<string> ReadAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        StringBuilder? data = null;

        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (data is not null)
                {
                    yield return data.ToString();
                    data = null;
                }
                continue;
            }
            if (line[0] == ':') continue;

            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            if (field != "data") continue;

            var value = colon < 0 ? "" : line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];
            if (data is null) data = new StringBuilder(value);
            else data.Append('\n').Append(value);
        }

        if (data is not null) yield return data.ToString();
    }
}
