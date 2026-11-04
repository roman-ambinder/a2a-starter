using System.Buffers;
using System.Globalization;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client;

/// <summary>
/// A byte buffer rented from <see cref="ArrayPool{T}"/>. Dispose it to return the rental. It is a class so that a copy of the
/// reference cannot return the array twice; <see cref="Dispose"/> is idempotent and safe to call from any thread.
/// The array is cleared on return because it holds an agent response.
/// </summary>
internal sealed class PooledBuffer : IDisposable
{
    private byte[]? _array;
    private readonly int _length;

    public PooledBuffer(byte[] array, int length)
    {
        ArgumentNullException.ThrowIfNull(array);
        _array = array;
        _length = length;
    }

    /// <summary>The bytes that were read. Empty after <see cref="Dispose"/>.</summary>
    public ReadOnlyMemory<byte> Memory => Volatile.Read(ref _array) is { } array ? new ReadOnlyMemory<byte>(array, 0, _length) : default;

    /// <summary>The bytes that were read. Empty after <see cref="Dispose"/>.</summary>
    public ReadOnlySpan<byte> Span => Memory.Span;

    /// <summary>Clears the array and returns it to the pool. Calling it again does nothing.</summary>
    public void Dispose()
    {
        var array = Interlocked.Exchange(ref _array, null);
        if (array is not null) ArrayPool<byte>.Shared.Return(array, clearArray: true);
    }
}

/// <summary>Reads an HTTP body with a hard size limit, so a hostile agent cannot make the client buffer more than the limit.</summary>
internal static class BoundedContent
{
    private const int ChunkSize = 16 * 1024;

    /// <summary>
    /// Reads the whole content. It checks <c>Content-Length</c> first, then reads in chunks and stops as soon as the total
    /// goes over <paramref name="limit"/>. At most <paramref name="limit"/> bytes are ever held.
    /// </summary>
    /// <param name="content">The content to read.</param>
    /// <param name="limit">The largest allowed size, in bytes.</param>
    /// <param name="what">What is being read, for the message (for example <c>Response body</c>).</param>
    /// <param name="agent">The client name, for the message.</param>
    /// <param name="option">
    /// The <see cref="A2AClientLimits"/> property to name in the message (the message then says <c>A2AClientOptions.Limits.{option}</c>),
    /// or a full option path that contains a dot (for example <c>A2ACardResolverOptions.MaxCardBytes</c>), which is named as given.
    /// </param>
    /// <param name="ct">A token that cancels the read.</param>
    /// <exception cref="A2AException">The content is larger than the limit (<see cref="A2AErrorCode.InvalidAgentResponse"/>).</exception>
    public static async ValueTask<PooledBuffer> ReadAsync(HttpContent content, long limit, string what, string agent, string option,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        // A buffer cannot be larger than Array.MaxLength, so a larger limit means the same as that size. This also keeps the (int) casts safe.
        limit = Math.Min(limit, Array.MaxLength);
        if (content.Headers.ContentLength is { } declared && declared > limit)
            throw TooLarge(what, agent, limit, option);

        var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var capacity = (int)Math.Min(limit, Math.Max(ChunkSize, Math.Min(content.Headers.ContentLength ?? 0, limit)));
        var buffer = ArrayPool<byte>.Shared.Rent(capacity);
        var length = 0;
        try
        {
            while (true)
            {
                if (length == buffer.Length || length == limit)
                {
                    if (length == limit)
                    {
                        // The buffer holds exactly the limit. Any further byte means the body is too large.
                        if (await ProbeMoreAsync(stream, ct).ConfigureAwait(false)) throw TooLarge(what, agent, limit, option);
                        break;
                    }
                    var grown = ArrayPool<byte>.Shared.Rent((int)Math.Min(limit, (long)buffer.Length * 2));
                    buffer.AsSpan(0, length).CopyTo(grown);
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                    buffer = grown;
                }

                var room = (int)Math.Min(buffer.Length - length, limit - length);
                var read = await stream.ReadAsync(buffer.AsMemory(length, room), ct).ConfigureAwait(false);
                if (read == 0) break;
                length += read;
            }
            return new PooledBuffer(buffer, length);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            throw;
        }
    }

    private static async ValueTask<bool> ProbeMoreAsync(Stream stream, CancellationToken ct)
    {
        var probe = ArrayPool<byte>.Shared.Rent(1);
        try
        {
            return await stream.ReadAsync(probe.AsMemory(0, 1), ct).ConfigureAwait(false) > 0;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(probe, clearArray: true);
        }
    }

    /// <summary>Builds the exception for a read that went over a limit.</summary>
    /// <param name="what">What was read, for the message.</param>
    /// <param name="agent">The client name, for the message.</param>
    /// <param name="limit">The limit that was exceeded, in bytes.</param>
    /// <param name="option">An <see cref="A2AClientLimits"/> property name, or a full option path that contains a dot.</param>
    public static A2AException TooLarge(string what, string agent, long limit, string option)
    {
        var path = option.Contains('.', StringComparison.Ordinal) ? option : $"A2AClientOptions.Limits.{option}";
        return new(A2AErrorCode.InvalidAgentResponse,
            $"{what} from agent '{agent}' is larger than {FormatSize(limit)}; raise {path} if this agent is trusted.");
    }

    /// <summary>Formats a size as <c>N MiB</c> or <c>N KiB</c> when it is an exact multiple, otherwise <c>N bytes</c>.</summary>
    public static string FormatSize(long bytes)
        => bytes > 0 && bytes % (1024 * 1024) == 0 ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024 * 1024)} MiB")
            : bytes > 0 && bytes % 1024 == 0 ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024} KiB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes");
}
