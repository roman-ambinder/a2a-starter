using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client.Auth;

/// <summary>Supplies the HTTP headers (or gRPC metadata) that authenticate a call to the agent described by a card.</summary>
/// <remarks>
/// The client calls <see cref="GetHeadersAsync"/> before every request, from any thread, and calls can overlap. An implementation
/// must be thread-safe. Cache tokens inside the implementation; the client does not. Register or pass one instance and
/// reuse it for the life of the client.
/// </remarks>
public interface IA2ACredentialProvider
{
    /// <summary>Gets the headers that authenticate the next call.</summary>
    /// <param name="card">The card of the agent that is about to be called.</param>
    /// <param name="ct">A token that cancels the call.</param>
    /// <returns>The header names and values to add to the request. Return an empty map to call anonymously.</returns>
    /// <exception cref="A2AAuthException">The provider could not get a credential (HTTP 401).</exception>
    ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct);
}
