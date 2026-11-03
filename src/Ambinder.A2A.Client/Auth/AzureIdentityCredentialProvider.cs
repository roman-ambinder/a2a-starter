using Ambinder.A2A.Protocol;
using Azure.Core;

namespace Ambinder.A2A.Client.Auth;

/// <summary>Gets bearer tokens from an Azure.Identity <see cref="TokenCredential"/> (which does its own caching).</summary>
public sealed class AzureIdentityCredentialProvider(TokenCredential credential, string scope) : IA2ACredentialProvider
{
    private readonly TokenCredential _credential = credential ?? throw new ArgumentNullException(nameof(credential));
    private readonly string _scope = scope ?? throw new ArgumentNullException(nameof(scope));

    public async ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct)
    {
        var token = await _credential.GetTokenAsync(new TokenRequestContext([_scope]), ct).ConfigureAwait(false);
        return new Dictionary<string, string> { ["Authorization"] = "Bearer " + token.Token };
    }
}
