namespace Ambinder.A2A.Client;

/// <summary>One request handed to an <see cref="IA2ATransport"/>.</summary>
/// <remarks>The client creates one instance per attempt. A transport reads it and does not keep it after the call.</remarks>
public sealed class A2ATransportRequest
{
    /// <summary>Gets the operation name, for example <c>SendMessage</c>.</summary>
    public required string Method { get; init; }

    /// <summary>Gets the request record, with the tenant already applied.</summary>
    public required object Body { get; init; }

    /// <summary>Gets the headers (or gRPC metadata) to send. They include <c>A2A-Extensions</c> but not <c>A2A-Version</c>.</summary>
    public required A2ARequestHeaders Headers { get; init; }

    /// <summary>
    /// Gets the <c>Major.Minor</c> protocol version of the selected interface. The transport writes the <c>A2A-Version</c> header
    /// from it. A 0.3 transport may omit the header.
    /// </summary>
    public required string ProtocolVersion { get; init; }

    /// <summary>Gets the retry safety, timeout and names for this call.</summary>
    public required A2ACallInfo CallInfo { get; init; }

    /// <summary>Gets a callback the transport calls once, when the response (or, for a stream, its headers) arrives. <see langword="null"/> when nobody listens.</summary>
    public Action<A2AResponseInfo>? OnHeaders { get; init; }
}
