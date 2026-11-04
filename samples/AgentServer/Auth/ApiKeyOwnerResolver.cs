using System.Security.Claims;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Security;

namespace AgentServer.Auth;

/// <summary>
/// The "other authentication schemes" pattern: owns tasks per API key name (<c>apikey:{name}</c>) and hands every other
/// caller to <see cref="DefaultOwnerResolver"/>.
/// </summary>
public sealed class ApiKeyOwnerResolver : IOwnerResolver
{
    private readonly DefaultOwnerResolver _default = new();

    public ValueTask<A2ACaller> ResolveAsync(ClaimsPrincipal user, AgentRegistration agent, CancellationToken ct)
    {
        if (user.Identity is { IsAuthenticated: true, AuthenticationType: ApiKeyAuthenticationHandler.SchemeName }
            && user.FindFirst(ApiKeyAuthenticationHandler.ApiKeyClaim)?.Value is { Length: > 0 } name)
        {
            return ValueTask.FromResult(new A2ACaller
            {
                Owner = OwnerKey.Parse($"apikey:{name}").Value,
                Kind = CallerKind.Other,
                Principal = user.Clone(),
            });
        }

        return _default.ResolveAsync(user, agent, ct);
    }
}
