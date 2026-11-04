using Ambinder.A2A.Client;
using Ambinder.A2A.Client.Tests.TestDoubles;
using Ambinder.A2A.Client.Transports;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client.Tests;

public class BoundedReadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const long MiB = 1024 * 1024;

    [Fact]
    public async Task Body_over_the_limit_throws_naming_the_option()
    {
        using var content = new StreamContent(new InfiniteStream());
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            BoundedContent.ReadAsync(content, 1024, "Response body", "billing", nameof(A2AClientLimits.MaxResponseBytes), Ct).AsTask());
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
        Assert.Equal("Response body from agent 'billing' is larger than 1 KiB; raise A2AClientOptions.Limits.MaxResponseBytes if this agent is trusted.", ex.Message);
    }

    [Fact]
    public async Task Content_length_over_the_limit_fails_before_reading()
    {
        using var content = new StreamContent(new ThrowingStream());
        content.Headers.ContentLength = 10 * MiB;
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            BoundedContent.ReadAsync(content, 8 * MiB, "Response body", "default", nameof(A2AClientLimits.MaxResponseBytes), Ct).AsTask());
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
        Assert.Contains("larger than 8 MiB", ex.Message);
    }

    [Fact]
    public async Task Oversized_body_allocates_a_bounded_amount()
    {
        using var content = new StreamContent(new InfiniteStream());
        var before = GC.GetAllocatedBytesForCurrentThread();
        await Assert.ThrowsAsync<A2AException>(() =>
            BoundedContent.ReadAsync(content, MiB, "Response body", "default", nameof(A2AClientLimits.MaxResponseBytes), Ct).AsTask());
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 4 * MiB, $"allocated {allocated} bytes");
    }

    [Fact]
    public async Task Hostile_100_MB_body_fails_after_reading_only_the_limit()
    {
        var counting = new CountingStream(100 * MiB);
        using var content = new StreamContent(counting);
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            BoundedContent.ReadAsync(content, 8 * MiB, "Response body", "evil", nameof(A2AClientLimits.MaxResponseBytes), Ct).AsTask());
        Assert.Contains("larger than 8 MiB", ex.Message);
        Assert.True(counting.BytesRead <= 8 * MiB + 1, $"read {counting.BytesRead}");
    }

    [Fact]
    public async Task Hostile_1_MB_error_body_fails_against_the_error_limit()
    {
        var counting = new CountingStream(MiB);
        using var content = new StreamContent(counting);
        var limits = new A2AClientLimits();
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            BoundedContent.ReadAsync(content, limits.MaxErrorBodyBytes, "Error body", "evil", nameof(A2AClientLimits.MaxErrorBodyBytes), Ct).AsTask());
        Assert.Equal("Error body from agent 'evil' is larger than 64 KiB; raise A2AClientOptions.Limits.MaxErrorBodyBytes if this agent is trusted.", ex.Message);
        Assert.True(counting.BytesRead <= limits.MaxErrorBodyBytes + 1, $"read {counting.BytesRead}");
    }

    [Fact]
    public async Task Hostile_10_MB_card_fails_against_a_1_MiB_limit()
    {
        var counting = new CountingStream(10 * MiB);
        using var content = new StreamContent(counting);
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            BoundedContent.ReadAsync(content, MiB, "Agent card", "evil", "MaxCardBytes", Ct).AsTask());
        Assert.Contains("Agent card from agent 'evil' is larger than 1 MiB", ex.Message);
        Assert.True(counting.BytesRead <= MiB + 1);
    }

    [Fact]
    public async Task Reading_under_the_limit_returns_pooled_bytes()
    {
        var expected = new byte[10 * 1024];
        new Random(7).NextBytes(expected);
        using var content = new ByteArrayContent(expected);
        using var buffer = await BoundedContent.ReadAsync(content, 8 * MiB, "Response body", "default", nameof(A2AClientLimits.MaxResponseBytes), Ct);
        Assert.True(buffer.Span.SequenceEqual(expected));
    }

    [Fact]
    public async Task A_body_exactly_at_the_limit_is_accepted_and_one_byte_more_is_not()
    {
        var exact = new byte[40_000];
        using (var content = new ByteArrayContent(exact))
        {
            using var buffer = await BoundedContent.ReadAsync(content, exact.Length, "Response body", "default", nameof(A2AClientLimits.MaxResponseBytes), Ct);
            Assert.Equal(exact.Length, buffer.Memory.Length);
        }
        using var over = new StreamContent(new CountingStream(exact.Length + 1));
        await Assert.ThrowsAsync<A2AException>(() =>
            BoundedContent.ReadAsync(over, exact.Length, "Response body", "default", nameof(A2AClientLimits.MaxResponseBytes), Ct).AsTask());
    }

    [Fact]
    public async Task Cancellation_is_not_turned_into_a_limit_error()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var content = new StreamContent(new CancelAwareStream());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BoundedContent.ReadAsync(content, MiB, "Response body", "default", nameof(A2AClientLimits.MaxResponseBytes), cts.Token).AsTask());
    }

    [Fact]
    public void Limits_default_to_the_spec_values()
    {
        var limits = new A2AClientLimits();
        Assert.Equal(8 * MiB, limits.MaxResponseBytes);
        Assert.Equal(4 * MiB, limits.MaxEventBytes);
        Assert.Equal(8 * MiB, limits.MaxGrpcMessageBytes);
        Assert.Equal(64 * 1024, limits.MaxErrorBodyBytes);
    }

    [Theory]
    [InlineData(1024L, "1 KiB")]
    [InlineData(65536L, "64 KiB")]
    [InlineData(4194304L, "4 MiB")]
    [InlineData(1000L, "1000 bytes")]
    public void Sizes_are_formatted_in_KiB_or_MiB(long bytes, string expected)
        => Assert.Equal(expected, BoundedContent.FormatSize(bytes));

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The body must not be read.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => throw new InvalidOperationException("The body must not be read.");
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CancelAwareStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return new(0);
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A generated stream of a fixed length that counts the bytes handed out. It holds no data.</summary>
    private sealed class CountingStream(long total) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Fill(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) => Fill(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => new(Fill(buffer.Span));

        private int Fill(Span<byte> buffer)
        {
            var n = (int)Math.Min(buffer.Length, total - BytesRead);
            buffer[..n].Fill((byte)'a');
            BytesRead += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
