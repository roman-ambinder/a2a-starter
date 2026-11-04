using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client;

/// <summary>Maps A2A requests to one wire protocol and back. Created by an <see cref="IA2ATransportFactory"/>.</summary>
/// <remarks>
/// <para>
/// Lifetime: the client creates one transport per selected interface, keeps it for the life of the client and disposes it
/// with the client. A transport must not dispose the <see cref="A2ATransportContext.HttpClient"/> it was given.
/// </para>
/// <para>
/// Concurrency: <see cref="SendAsync"/> and <see cref="StreamAsync"/> are called from any thread, often at the same time, so
/// implementations must be thread-safe and keep no per-call state outside the call.
/// </para>
/// <para>
/// A transport does not add interceptors, credentials, retries, timeouts or tracing. The client wraps those around every call.
/// </para>
/// </remarks>
public interface IA2ATransport : IAsyncDisposable
{
    /// <summary>Sends a unary request and returns the response.</summary>
    /// <param name="request">The request to send.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The response record and facts about the response.</returns>
    ValueTask<A2ATransportResponse> SendAsync(A2ATransportRequest request, CancellationToken ct);

    /// <summary>Sends a streaming request and yields the events of the response.</summary>
    /// <param name="request">The request to send. The transport calls <see cref="A2ATransportRequest.OnHeaders"/> once the response headers arrive.</param>
    /// <param name="ct">A token that cancels the stream.</param>
    /// <returns>The stream events, in order.</returns>
    IAsyncEnumerable<StreamResponse> StreamAsync(A2ATransportRequest request, CancellationToken ct);
}
