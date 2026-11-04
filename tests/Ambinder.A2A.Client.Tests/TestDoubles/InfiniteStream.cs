namespace Ambinder.A2A.Client.Tests.TestDoubles;

/// <summary>A read-only stream that returns <c>'a'</c> bytes forever.</summary>
public sealed class InfiniteStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Fill(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer) => Fill(buffer);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => new(Fill(buffer.Span));

    private static int Fill(Span<byte> buffer)
    {
        buffer.Fill((byte)'a');
        return buffer.Length;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
