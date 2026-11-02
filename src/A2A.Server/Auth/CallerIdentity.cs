using System.Security.Claims;

namespace A2A.Server.Auth;

/// <summary>Derives the task owner key from the caller.</summary>
public static class CallerIdentity
{
    /// <summary>Claim set by the API key handler; value = key name.</summary>
    public const string ApiKeyClaim = "a2a:apikey";

    /// <summary>Entra tenant id claim (short form).</summary>
    public const string TenantIdClaim = "tid";

    /// <summary>Entra tenant id claim as mapped by the JWT handler when inbound claim mapping is on.</summary>
    public const string MappedTenantIdClaim = "http://schemas.microsoft.com/identity/claims/tenantid";

    public const string Anonymous = "anonymous";

    /// <summary>
    /// API key → <c>apikey:{name}</c>; bearer → <c>tenant:{tid}</c> (ownership is tenant-scoped);
    /// unauthenticated → <c>anonymous</c>. An authenticated caller with neither claim gets
    /// <c>user:{subject}</c> so it never shares tasks with anonymous callers.
    /// </summary>
    public static string GetOwner(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Identity?.IsAuthenticated != true) return Anonymous;

        if (Value(user, ApiKeyClaim) is { } key) return $"apikey:{key}";
        if ((Value(user, TenantIdClaim) ?? Value(user, MappedTenantIdClaim)) is { } tid) return $"tenant:{tid}";
        if ((Value(user, "sub") ?? Value(user, ClaimTypes.NameIdentifier) ?? user.Identity.Name) is { } sub) return $"user:{sub}";
        return Anonymous;
    }

    private static string? Value(ClaimsPrincipal user, string type)
        => user.FindFirst(type)?.Value is { Length: > 0 } v ? v : null;
}
