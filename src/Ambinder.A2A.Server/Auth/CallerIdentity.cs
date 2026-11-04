using System.Security.Claims;

namespace Ambinder.A2A.Server.Auth;

/// <summary>Owner-key constants and a static shortcut to <see cref="DefaultOwnerResolver"/>. The server itself uses the registered <see cref="IOwnerResolver"/>.</summary>
public static class CallerIdentity
{
    /// <summary>Claim set by the API key handler; value = key name.</summary>
    public const string ApiKeyClaim = "a2a:apikey";

    /// <summary>Entra tenant id claim (short form).</summary>
    public const string TenantIdClaim = "tid";

    /// <summary>Entra tenant id claim as mapped by the JWT handler when inbound claim mapping is on.</summary>
    public const string MappedTenantIdClaim = "http://schemas.microsoft.com/identity/claims/tenantid";

    /// <summary>The owner key of an unauthenticated caller: <c>anonymous</c>.</summary>
    public const string Anonymous = "anonymous";

    /// <summary>The owner key from <see cref="DefaultOwnerResolver.Instance"/>. See <see cref="DefaultOwnerResolver"/> for the rules.</summary>
    /// <param name="user">The caller.</param>
    /// <returns>The owner key.</returns>
    /// <exception cref="Protocol.A2AAuthException">The caller is authenticated but has no usable identifier (HTTP 401).</exception>
    public static string GetOwner(ClaimsPrincipal user) => DefaultOwnerResolver.Instance.GetOwner(user);
}
