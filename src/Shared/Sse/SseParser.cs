using System.Buffers;
using System.IO.Pipelines;
using Ambinder.A2A.Client;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Sse;

/// <summary>
/// Parses Server-Sent Events straight from a <see cref="PipeReader"/>, with no string per line. Compiled as internal source into each
/// assembly that reads SSE (spec 01 rule 10), so every stream gets the same limits.
/// </summary>
internal static class SseParser
{
    // Room for the "data: " prefix and the line end on top of a data value that is exactly at the limit.
    private const int LineSlack = 1024;

    /// <summary>
    /// Calls <paramref name="onEvent"/> once per event that has data lines. The data bytes (lines joined with <c>\n</c>) are handed over
    /// without a string; the buffer is valid only during the callback. Comments and the <c>event</c>, <c>id</c> and <c>retry</c> fields
    /// are ignored. Handles CRLF, LF and CR, and a last event without a closing blank line.
    /// </summary>
    /// <param name="reader">The reader to parse. The caller owns it and completes it.</param>
    /// <param name="maxEventBytes">The largest event (data lines joined), in bytes. Going over throws <see cref="A2AException"/>.</param>
    /// <param name="idle">How long to wait for bytes. Any byte, including a comment, counts. <see cref="Timeout.InfiniteTimeSpan"/> turns it off.</param>
    /// <param name="time">The clock for the idle timeout.</param>
    /// <param name="onEvent">Called with the data of each event. The idle timer is paused while it runs.</param>
    /// <param name="ct">A token that cancels the read.</param>
    /// <param name="agent">The client name, for messages.</param>
    /// <exception cref="A2AException">An event or a line is larger than <paramref name="maxEventBytes"/> (<see cref="A2AErrorCode.InvalidAgentResponse"/>).</exception>
    /// <exception cref="A2ATimeoutException">No bytes arrived within <paramref name="idle"/>.</exception>
    public static async ValueTask ReadAsync(PipeReader reader, int maxEventBytes, TimeSpan idle, TimeProvider time,
        Func<ReadOnlySequence<byte>, CancellationToken, ValueTask> onEvent, CancellationToken ct, string agent = "default")
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(onEvent);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEventBytes);

        var idleEnabled = idle > TimeSpan.Zero && idle != Timeout.InfiniteTimeSpan;
        using var idleCts = new CancellationTokenSource(Timeout.InfiniteTimeSpan, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, idleCts.Token);
        var state = new State(maxEventBytes, agent);

        while (true)
        {
            ReadResult result;
            try
            {
                if (idleEnabled)
                {
                    idleCts.TryReset();
                    idleCts.CancelAfter(idle);
                }
                result = await reader.ReadAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (idleEnabled && idleCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new A2ATimeoutException("Stream", idle, "A2AClientOptions.Timeouts.StreamIdleTimeout");
            }

            // The caller's callback may take a while. That is not the agent's silence.
            idleCts.CancelAfter(Timeout.InfiniteTimeSpan);
            ct.ThrowIfCancellationRequested();

            var buffer = result.Buffer;
            var consumed = await state.ParseAsync(buffer, result.IsCompleted, onEvent, ct).ConfigureAwait(false);
            if (result.IsCompleted)
            {
                reader.AdvanceTo(buffer.End);
                return;
            }
            reader.AdvanceTo(consumed, buffer.End);
        }
    }

    private sealed class State(int maxEventBytes, string agent)
    {
        private static ReadOnlySpan<byte> LineEnds => "\r\n"u8;

        private ArrayBufferWriter<byte>? _joined;
        private SequencePosition _pos;
        private bool _hasPos;
        private long _scanned;          // bytes of the pending partial line that are known to hold no line end
        private long _eventRaw;         // raw bytes of the event so far, including ignored lines and line ends
        private long _dataBytes;        // data bytes so far, with the '\n' joins
        private int _dataLines;
        private SequencePosition _firstStart, _firstEnd;

        /// <summary>Parses what is in the buffer. Returns the position the reader can consume up to.</summary>
        public async ValueTask<SequencePosition> ParseAsync(ReadOnlySequence<byte> buffer, bool completed,
            Func<ReadOnlySequence<byte>, CancellationToken, ValueTask> onEvent, CancellationToken ct)
        {
            if (!_hasPos) { _pos = buffer.Start; _hasPos = true; }
            var consumed = buffer.Start;
            var atConsumed = !_holdsData;

            while (true)
            {
                var rest = buffer.Slice(_pos);
                if (!TryNextLine(rest, completed, out var line, out var lineEndLength))
                {
                    var pending = rest.Length;
                    if (_eventRaw + pending > (long)maxEventBytes + LineSlack) throw TooLarge();
                    break;
                }

                var advance = line.Length + lineEndLength;
                _pos = rest.GetPosition(advance);
                _scanned = 0;
                atConsumed = false;
                _eventRaw += advance;
                if (_eventRaw > (long)maxEventBytes + LineSlack) throw TooLarge();

                if (line.Length == 0)
                {
                    await DispatchAsync(buffer, onEvent, ct).ConfigureAwait(false);
                    consumed = _pos;
                    atConsumed = true;
                }
                else if (TryGetDataValue(line, out var value))
                {
                    AddData(buffer, value);
                }
                else if (_dataLines == 0)
                {
                    // A comment or an ignored field before any data: nothing to keep.
                    _eventRaw = 0;
                    consumed = _pos;
                    atConsumed = true;
                }
            }

            if (completed)
            {
                await DispatchAsync(buffer, onEvent, ct).ConfigureAwait(false);
                consumed = buffer.End;
            }
            // The reader may hand out different segment objects after it consumes. When nothing is held back,
            // start again from the start of the next buffer instead of a position from this one.
            _holdsData = _dataLines > 0;
            if (atConsumed) _hasPos = false;
            return consumed;
        }

        private bool _holdsData;

        private bool TryNextLine(ReadOnlySequence<byte> rest, bool completed, out ReadOnlySequence<byte> line, out int lineEndLength)
        {
            line = default;
            lineEndLength = 0;
            if (rest.IsEmpty) return false;

            var scan = new SequenceReader<byte>(rest);
            if (_scanned > 0) scan.Advance(_scanned);
            if (!scan.TryReadToAny(out ReadOnlySequence<byte> _, LineEnds, advancePastDelimiter: false))
            {
                if (!completed)
                {
                    _scanned = rest.Length;
                    return false;
                }
                // The stream ended in the middle of a line. Take it as a line.
                line = rest;
                return true;
            }

            var lineLength = scan.Consumed;
            scan.TryPeek(out var delimiter);
            if (delimiter == (byte)'\n')
            {
                lineEndLength = 1;
            }
            else
            {
                scan.Advance(1);
                if (scan.TryPeek(out var next)) lineEndLength = next == (byte)'\n' ? 2 : 1;
                else if (completed) lineEndLength = 1;
                else
                {
                    // A '\r' at the end of the buffer may be the first half of CRLF. Wait for the next read.
                    _scanned = lineLength;
                    return false;
                }
            }
            line = rest.Slice(0, lineLength);
            return true;
        }

        private static bool TryGetDataValue(in ReadOnlySequence<byte> line, out ReadOnlySequence<byte> value)
        {
            value = default;
            Span<byte> head = stackalloc byte[6];
            var headLength = (int)Math.Min(head.Length, line.Length);
            line.Slice(0, headLength).CopyTo(head);
            if (headLength < 4 || !head[..4].SequenceEqual("data"u8)) return false;
            if (headLength > 4 && head[4] != (byte)':') return false;

            var offset = headLength == 4 ? 4 : 5;
            if (headLength == 6 && head[5] == (byte)' ') offset = 6;
            value = line.Slice(offset);
            return true;
        }

        private void AddData(in ReadOnlySequence<byte> buffer, in ReadOnlySequence<byte> value)
        {
            var total = _dataBytes + (_dataLines > 0 ? 1 : 0) + value.Length;
            if (total > maxEventBytes) throw TooLarge();

            if (_dataLines == 0)
            {
                _firstStart = value.Start;
                _firstEnd = value.End;
            }
            else
            {
                _joined ??= new ArrayBufferWriter<byte>();
                if (_dataLines == 1) Append(_joined, buffer.Slice(_firstStart, _firstEnd));
                _joined.GetSpan(1)[0] = (byte)'\n';
                _joined.Advance(1);
                Append(_joined, value);
            }
            _dataBytes = total;
            _dataLines++;
        }

        private static void Append(ArrayBufferWriter<byte> writer, in ReadOnlySequence<byte> sequence)
        {
            foreach (var segment in sequence)
            {
                writer.Write(segment.Span);
            }
        }

        private async ValueTask DispatchAsync(ReadOnlySequence<byte> buffer,
            Func<ReadOnlySequence<byte>, CancellationToken, ValueTask> onEvent, CancellationToken ct)
        {
            var lines = _dataLines;
            if (lines > 0)
            {
                var data = lines == 1 ? buffer.Slice(_firstStart, _firstEnd) : new ReadOnlySequence<byte>(_joined!.WrittenMemory);
                try
                {
                    await onEvent(data, ct).ConfigureAwait(false);
                }
                finally
                {
                    Reset();
                }
            }
            else
            {
                Reset();
            }
        }

        private void Reset()
        {
            _joined?.ResetWrittenCount();
            _dataLines = 0;
            _dataBytes = 0;
            _eventRaw = 0;
        }

        private A2AException TooLarge() => BoundedContent.TooLarge("SSE event", agent, maxEventBytes, "MaxEventBytes");
    }
}
