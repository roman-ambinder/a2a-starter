using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;

namespace Ambinder.A2A.Benchmarks.Hosts;

/// <summary>
/// A fake <see cref="HttpMessageHandler"/> that answers every request with the same pre-built bytes, so a client-only benchmark
/// measures the client (request building, response parsing) and nothing else. For gRPC the bytes are one length-prefixed message
/// and the answer is HTTP/2 with the <c>grpc-status: 0</c> trailer.
/// </summary>
internal sealed class CannedHandler : HttpMessageHandler
{
    private readonly byte[] _body;
    private readonly string _contentType;
    private readonly bool _grpc;

    private CannedHandler(byte[] body, string contentType, bool grpc)
    {
        _body = body;
        _contentType = contentType;
        _grpc = grpc;
    }

    /// <summary>A JSON answer (JSON-RPC envelope or REST body).</summary>
    public static CannedHandler Json(byte[] body) => new(body, "application/json", grpc: false);

    /// <summary>A gRPC answer: the serialized protobuf <paramref name="message"/> in one frame.</summary>
    public static CannedHandler Grpc(byte[] message)
    {
        var frame = new byte[5 + message.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1, 4), (uint)message.Length);
        message.CopyTo(frame, 5);
        return new CannedHandler(frame, "application/grpc", grpc: true);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_body), RequestMessage = request };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(_contentType);
        if (_grpc)
        {
            response.Version = HttpVersion.Version20;
            response.TrailingHeaders.Add("grpc-status", "0");
        }

        return Task.FromResult(response);
    }
}
