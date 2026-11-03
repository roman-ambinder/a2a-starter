using A2A.Protocol;

namespace A2A.Client.Auth;

/// <summary>Supplies the HTTP headers (or gRPC metadata) that authenticate a call to the agent described by a card.</summary>
public interface IA2ACredentialProvider
{
    ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct);
}
