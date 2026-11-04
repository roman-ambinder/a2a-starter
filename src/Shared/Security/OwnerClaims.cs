using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace Ambinder.A2A.Server.Security;

/// <summary>
/// Reads the Entra claims behind <see cref="OwnerKey"/>: tenant id, app id, user object id and whether the token is delegated.
/// Short claim names (<c>MapInboundClaims = false</c>) come first; the mapped long names are accepted as a fallback.
/// Compiled into every assembly that needs it, so the rate limiter reads the same claims as the owner resolver.
/// </summary>
internal static class OwnerClaims
{
    private const string TenantIdShort = "tid";
    private const string TenantIdMapped = "http://schemas.microsoft.com/identity/claims/tenantid";
    private const string ObjectIdShort = "oid";
    private const string ObjectIdMapped = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string ScopeShort = "scp";
    private const string ScopeMapped = "http://schemas.microsoft.com/identity/claims/scope";

    public static string? TenantId(ClaimsPrincipal user) => First(user, TenantIdShort, TenantIdMapped);

    /// <summary>The app id of the token: <c>azp</c> (v2), else <c>appid</c> (v1).</summary>
    public static string? AppId(ClaimsPrincipal user) => First(user, "azp", "appid");

    public static string? ObjectId(ClaimsPrincipal user) => First(user, ObjectIdShort, ObjectIdMapped);

    /// <summary>True when the token carries a scope claim, which means a user signed in (delegated token).</summary>
    public static bool IsDelegated(ClaimsPrincipal user) => First(user, ScopeShort, ScopeMapped) is not null;

    /// <summary>
    /// The key a rate limiter partitions by, from token claims only: <c>tid:{tid}/app:{appid}</c> for an app-only token,
    /// <c>tid:{tid}/user:{oid}</c> for a delegated token. False when the principal has no tenant or no matching id.
    /// </summary>
    public static bool TryGetPartitionKey(ClaimsPrincipal user, [NotNullWhen(true)] out string? key)
    {
        key = null;
        if (user.Identity?.IsAuthenticated != true || TenantId(user) is not { } tid) return false;
        var id = IsDelegated(user) ? ObjectId(user) : AppId(user);
        if (id is null || tid.Contains('/') || id.Contains('/')) return false;
        var kind = IsDelegated(user) ? "user" : "app";
        key = $"tid:{tid}/{kind}:{id}";
        return key.Length <= 256;
    }

    /// <summary>
    /// A deep copy of the principal: new identities with new claims, so later changes to the original (or its identities) do not
    /// show in the copy. The agent run outlives the request, so it must not share state with <c>HttpContext.User</c>.
    /// </summary>
    public static ClaimsPrincipal Snapshot(ClaimsPrincipal user)
        => new(user.Identities.Select(identity => new ClaimsIdentity(
            identity.Claims.Select(c => new Claim(c.Type, c.Value, c.ValueType, c.Issuer, c.OriginalIssuer)),
            identity.AuthenticationType, identity.NameClaimType, identity.RoleClaimType)));

    private static string? First(ClaimsPrincipal user, string type, string fallback)
        => Value(user, type) ?? Value(user, fallback);

    private static string? Value(ClaimsPrincipal user, string type)
        => user.FindFirst(type)?.Value is { Length: > 0 } v ? v : null;
}
