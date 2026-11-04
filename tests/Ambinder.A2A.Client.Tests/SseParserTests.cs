using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Ambinder.A2A.Client;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Sse;
using Microsoft.Extensions.Time.Testing;

namespace Ambinder.A2A.Client.Tests;

public class SseParserTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const int MiB = 1024 * 1024;

    private static async Task<List<string>> ParseAsync(byte[] raw, int maxEventBytes = 4 * MiB)
    {
        var reader = PipeReader.Create(new MemoryStream(raw));
        return await ParseAsync(reader, maxEventBytes);
    }

    private static async Task<List<string>> ParseAsync(PipeReader reader, int maxEventBytes = 4 * MiB)
    {
        var events = new List<string>();
        await SseParser.ReadAsync(reader, maxEventBytes, Timeout.InfiniteTimeSpan, TimeProvider.System, (data, _) =>
        {
            events.Add(Encoding.UTF8.GetString(data));
            return default;
        }, Ct);
        return events;
    }

    [Fact]
    public async Task Handles_multiline_and_crlf_and_last_event_without_blank_line()
    {
        const string raw =
            ": comment\r\n" +
            "event: message\r\n" +
            "id: 1\r\n" +
            "retry: 5\r\n" +
            "data: one\r\n" +
            "\r\n" +
            "data:two-a\n" +
            "data: two-b\n" +
            "\n" +
            "data\n" +
            "\n" +
            "\n" +
            "data: last-without-blank-line";

        Assert.Equal(["one", "two-a\ntwo-b", "", "last-without-blank-line"], await ParseAsync(Encoding.UTF8.GetBytes(raw)));
    }

    [Fact]
    public async Task Handles_bare_CR_line_ends()
    {
        Assert.Equal(["a\nb", "c"], await ParseAsync("data: a\rdata: b\r\rdata: c\r\r"u8.ToArray()));
    }

    [Fact]
    public async Task Ignores_fields_that_only_start_with_data()
    {
        Assert.Equal(["x"], await ParseAsync("database: no\ndata: x\n\n"u8.ToArray()));
    }

    [Fact]
    public async Task Skips_only_one_space_after_the_colon()
    {
        Assert.Equal(["  two"], await ParseAsync("data:   two\n\n"u8.ToArray()));
    }

    [Fact]
    public async Task Crlf_split_across_reads_is_one_line_end()
    {
        var pipe = new Pipe();
        var parse = ParseAsync(pipe.Reader);
        await pipe.Writer.WriteAsync("data: a\r"u8.ToArray(), Ct);
        await pipe.Writer.WriteAsync("\n\r"u8.ToArray(), Ct);
        await pipe.Writer.WriteAsync("\ndata: b\r\n\r\n"u8.ToArray(), Ct);
        await pipe.Writer.CompleteAsync();
        Assert.Equal(["a", "b"], await parse);
    }

    [Fact]
    public async Task Event_split_across_many_small_reads_is_assembled()
    {
        var pipe = new Pipe();
        var parse = ParseAsync(pipe.Reader);
        foreach (var b in Encoding.UTF8.GetBytes("data: {\"a\":1}\ndata: {\"b\":2}\n\ndata: z\n\n"))
            await pipe.Writer.WriteAsync(new[] { b }, Ct);
        await pipe.Writer.CompleteAsync();
        Assert.Equal(["{\"a\":1}\n{\"b\":2}", "z"], await parse);
    }

    [Fact]
    public async Task Event_over_MaxEventBytes_throws_named_limit()
    {
        var raw = Encoding.UTF8.GetBytes("data: " + new string('a', 5 * MiB) + "\n\n");
        var ex = await Assert.ThrowsAsync<A2AException>(() => ParseAsync(raw));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
        Assert.Equal("SSE event from agent 'default' is larger than 4 MiB; raise A2AClientOptions.Limits.MaxEventBytes if this agent is trusted.", ex.Message);
    }

    [Fact]
    public async Task Joined_data_lines_over_MaxEventBytes_throw()
    {
        var line = "data: " + new string('a', 600) + "\n";
        var raw = Encoding.UTF8.GetBytes(line + line + "\n");
        var ex = await Assert.ThrowsAsync<A2AException>(() => ParseAsync(raw, maxEventBytes: 1000));
        Assert.Contains("MaxEventBytes", ex.Message);
    }

    [Fact]
    public async Task Event_exactly_at_the_limit_is_accepted()
    {
        var value = new string('a', 1000);
        Assert.Equal([value], await ParseAsync(Encoding.UTF8.GetBytes("data: " + value + "\r\n\r\n"), maxEventBytes: 1000));
    }

    [Fact]
    public async Task Line_without_newline_over_limit_throws()
    {
        var raw = new byte[5 * MiB];
        Array.Fill(raw, (byte)'a');
        var ex = await Assert.ThrowsAsync<A2AException>(() => ParseAsync(raw));
        Assert.Contains("MaxEventBytes", ex.Message);
    }

    [Fact]
    public async Task Oversized_event_allocates_a_bounded_amount()
    {
        var raw = new byte[5 * MiB];
        Array.Fill(raw, (byte)'a');
        // Warm up pools and JIT so that the measurement sees steady state.
        await Assert.ThrowsAsync<A2AException>(() => ParseAsync(raw));

        var before = GC.GetAllocatedBytesForCurrentThread();
        await Assert.ThrowsAsync<A2AException>(() => ParseAsync(raw));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 2L * 4 * MiB, $"allocated {allocated} bytes");
    }

    [Fact]
    public async Task Agent_name_appears_in_the_limit_message()
    {
        var reader = PipeReader.Create(new MemoryStream(Encoding.UTF8.GetBytes("data: " + new string('a', 3000) + "\n\n")));
        var ex = await Assert.ThrowsAsync<A2AException>(() => SseParser.ReadAsync(reader, 1024, Timeout.InfiniteTimeSpan,
            TimeProvider.System, (_, _) => default, Ct, agent: "billing").AsTask());
        Assert.StartsWith("SSE event from agent 'billing' is larger than 1 KiB;", ex.Message);
    }

    [Fact]
    public async Task Comment_only_bytes_count_as_activity()
    {
        var time = new FakeTimeProvider();
        var pipe = new Pipe();
        var reader = new SignalingPipeReader(pipe.Reader);
        var events = new List<string>();
        var first = reader.NextReadAsync();
        var task = SseParser.ReadAsync(reader, 1024, TimeSpan.FromMinutes(5), time, (d, _) =>
        {
            events.Add(Encoding.UTF8.GetString(d));
            return default;
        }, Ct).AsTask();
        await first;

        for (var i = 0; i < 3; i++)
        {
            // Wait until the parser waits for bytes again, so that the idle timer has been re-armed.
            var next = reader.NextReadAsync();
            time.Advance(TimeSpan.FromMinutes(4));
            await pipe.Writer.WriteAsync(":\n"u8.ToArray(), Ct);
            await next;
        }
        await pipe.Writer.WriteAsync("data: ok\n\n"u8.ToArray(), Ct);
        await pipe.Writer.CompleteAsync();
        await task;
        Assert.Equal(["ok"], events);
    }

    private sealed class SignalingPipeReader(PipeReader inner) : PipeReader
    {
        private TaskCompletionSource _next = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task NextReadAsync()
        {
            _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _next.Task;
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            var result = inner.ReadAsync(cancellationToken);
            _next.TrySetResult();
            return result;
        }

        public override bool TryRead(out ReadResult result) => inner.TryRead(out result);
        public override void AdvanceTo(SequencePosition consumed) => inner.AdvanceTo(consumed);
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => inner.AdvanceTo(consumed, examined);
        public override void CancelPendingRead() => inner.CancelPendingRead();
        public override void Complete(Exception? exception = null) => inner.Complete(exception);
    }

    [Fact]
    public async Task Time_in_the_callback_is_not_idle_time()
    {
        var time = new FakeTimeProvider();
        var pipe = new Pipe();
        var task = SseParser.ReadAsync(pipe.Reader, 1024, TimeSpan.FromMinutes(5), time, (_, _) =>
        {
            time.Advance(TimeSpan.FromMinutes(30));
            return default;
        }, Ct).AsTask();

        await pipe.Writer.WriteAsync("data: slow consumer\n\ndata: next\n\n"u8.ToArray(), Ct);
        await pipe.Writer.CompleteAsync();
        await task;
    }

    [Fact]
    public async Task Data_bytes_are_passed_without_string_conversion()
    {
        const string json = """{"task":{"id":"t1"}}""";
        var expected = Encoding.UTF8.GetBytes(json);
        byte[]? seen = null;
        var reader = PipeReader.Create(new MemoryStream(Encoding.UTF8.GetBytes("data: " + json + "\n\n")));
        await SseParser.ReadAsync(reader, 1024, Timeout.InfiniteTimeSpan, TimeProvider.System, (data, _) =>
        {
            seen = data.ToArray();
            return default;
        }, Ct);
        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task Single_data_line_is_a_slice_of_the_input_buffer()
    {
        var reader = PipeReader.Create(new MemoryStream("data: abc\n\n"u8.ToArray()));
        var singleSegment = false;
        await SseParser.ReadAsync(reader, 1024, Timeout.InfiniteTimeSpan, TimeProvider.System, (data, _) =>
        {
            singleSegment = data.IsSingleSegment;
            return default;
        }, Ct);
        Assert.True(singleSegment);
    }

    [Fact]
    public async Task Idle_timeout_fires_when_no_bytes_arrive()
    {
        var time = new FakeTimeProvider();
        var pipe = new Pipe();
        var task = SseParser.ReadAsync(pipe.Reader, maxEventBytes: 1024, idle: TimeSpan.FromMinutes(5), time, (_, _) => default, Ct).AsTask();
        time.Advance(TimeSpan.FromMinutes(5));
        var ex = await Assert.ThrowsAsync<A2ATimeoutException>(() => task);
        Assert.Contains("StreamIdleTimeout", ex.Message);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_a_timeout()
    {
        var pipe = new Pipe();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = SseParser.ReadAsync(pipe.Reader, 1024, TimeSpan.FromMinutes(5), new FakeTimeProvider(), (_, _) => default, cts.Token).AsTask();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
