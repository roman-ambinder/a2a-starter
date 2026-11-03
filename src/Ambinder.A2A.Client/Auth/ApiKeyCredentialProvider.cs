using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client.Auth;

/// <summary>
/// Sends an API key in the header named by the card's <c>apiKeySecurityScheme</c>.
/// Phase 1 supports <c>location = "header"</c> only.
/// </summary>
public sealed class ApiKeyCredentialProvider(string key) : IA2ACredentialProvider
{
    private readonly string _key = key ?? throw new ArgumentNullException(nameof(key));

    public ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct)
    {
        var scheme = card.SecuritySchemes?.Values.Select(s => s.ApiKeySecurityScheme).FirstOrDefault(s => s is not null)
            ?? throw new InvalidOperationException($"Agent card '{card.Name}' declares no apiKeySecurityScheme.");

        if (!string.Equals(scheme.Location, "header", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException(
                $"API key location '{scheme.Location}' is not supported in Phase 1; only 'header' is.");
        if (scheme.Name.Length == 0)
            throw new InvalidOperationException("The apiKeySecurityScheme has no header name.");

        return new(new Dictionary<string, string> { [scheme.Name] = _key });
    }
}
