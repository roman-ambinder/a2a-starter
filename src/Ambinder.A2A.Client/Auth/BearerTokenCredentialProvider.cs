using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client.Auth;

/// <summary>Sends a fixed <c>Authorization: Bearer</c> token.</summary>
/// <param name="token">The bearer token. It is a secret; do not log it.</param>
public sealed class BearerTokenCredentialProvider(string token) : IA2ACredentialProvider
{
    private readonly IReadOnlyDictionary<string, string> _headers = new Dictionary<string, string>
    {
        ["Authorization"] = "Bearer " + (token ?? throw new ArgumentNullException(nameof(token))),
    };

    /// <inheritdoc/>
    public ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct) => new(_headers);
}
