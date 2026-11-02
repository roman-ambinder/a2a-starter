using A2A.Protocol;

namespace A2A.Client.Auth;

/// <summary>Sends a fixed <c>Authorization: Bearer</c> token.</summary>
public sealed class BearerTokenCredentialProvider(string token) : IA2ACredentialProvider
{
    private readonly IReadOnlyDictionary<string, string> _headers = new Dictionary<string, string>
    {
        ["Authorization"] = "Bearer " + (token ?? throw new ArgumentNullException(nameof(token))),
    };

    public ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct) => new(_headers);
}
