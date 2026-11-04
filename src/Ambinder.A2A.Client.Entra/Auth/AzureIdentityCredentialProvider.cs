using Ambinder.A2A.Client.Auth;
using Ambinder.A2A.Protocol;
using Azure.Core;

namespace Ambinder.A2A.Client.Entra;

/// <summary>Gets bearer tokens from an Azure.Identity <see cref="TokenCredential"/> (which does its own caching).</summary>
/// <remarks>The provider is thread-safe when the credential is. Create one and reuse it.</remarks>
/// <param name="credential">The Azure.Identity credential that gets the tokens, for example <c>DefaultAzureCredential</c>.</param>
/// <param name="scope">The scope to request, for example <c>api://my-agent/.default</c>.</param>
/// <example>
/// <code>
/// // requires: AgentCard card;
/// var credentials = new AzureIdentityCredentialProvider(new DefaultAzureCredential(), "api://my-agent/.default");
/// var options = new A2AClientOptions { Credentials = credentials };
/// await using IA2AClient client = A2AClientFactory.Create(card, options);
/// </code>
/// </example>
public sealed class AzureIdentityCredentialProvider(TokenCredential credential, string scope) : IA2ACredentialProvider
{
    private readonly TokenCredential _credential = credential ?? throw new ArgumentNullException(nameof(credential));
    private readonly string _scope = scope ?? throw new ArgumentNullException(nameof(scope));

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct)
    {
        var token = await _credential.GetTokenAsync(new TokenRequestContext([_scope]), ct).ConfigureAwait(false);
        return new Dictionary<string, string> { ["Authorization"] = "Bearer " + token.Token };
    }
}
