using System.Security.Claims;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Auth;

/// <summary>
/// Default task ownership: one client app in one tenant.
/// <list type="bullet">
/// <item>Unauthenticated → <c>anonymous</c>.</item>
/// <item>API key (primary identity's <see cref="ClaimsIdentity.AuthenticationType"/> is <see cref="ApiKeyAuthenticationType"/> and it
/// carries <see cref="CallerIdentity.ApiKeyClaim"/>) → <c>apikey:{name}</c>. The claim on any other identity is ignored.</item>
/// <item>Bearer with a tenant (<c>tid</c>) → <c>tenant:{tid}/app:{id}</c>, id = <c>azp</c>, else <c>appid</c>, else <c>sub</c>, else <c>oid</c>.</item>
/// <item>Bearer without a tenant → <c>user:{id}</c> with the same id order (plus the name identifier and name).</item>
/// <item>Authenticated with no identifier at all → <see cref="A2AAuthException"/> (401). Never a shared fallback key.</item>
/// </list>
/// Step-up rule (<see cref="CanContinueInterrupted"/>): an <c>AUTH_REQUIRED</c> task may be continued by another app of the same tenant.
/// </summary>
/// <remarks>The resolver has no state and is thread-safe. The server uses one shared instance unless you register your own <see cref="IOwnerResolver"/>.</remarks>
public sealed class DefaultOwnerResolver : IOwnerResolver
{
    /// <summary>Authentication type of the API key identity (the sample's <c>ApiKey</c> scheme).</summary>
    public const string DefaultApiKeyAuthenticationType = "ApiKey";

    /// <summary>The instance <see cref="CallerIdentity"/> uses.</summary>
    public static DefaultOwnerResolver Instance { get; } = new();

    private const string ObjectIdClaim = "oid";
    private const string MappedObjectIdClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    /// <summary>Creates a resolver.</summary>
    /// <param name="apiKeyAuthenticationType">The <see cref="ClaimsIdentity.AuthenticationType"/> that marks an API key identity.</param>
    /// <exception cref="ArgumentException"><paramref name="apiKeyAuthenticationType"/> is <see langword="null"/> or empty.</exception>
    public DefaultOwnerResolver(string apiKeyAuthenticationType = DefaultApiKeyAuthenticationType)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKeyAuthenticationType);
        ApiKeyAuthenticationType = apiKeyAuthenticationType;
    }

    /// <summary>The <see cref="ClaimsIdentity.AuthenticationType"/> that marks an API key identity. The default is <c>ApiKey</c>.</summary>
    public string ApiKeyAuthenticationType { get; }

    /// <inheritdoc/>
    public string GetOwner(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identity = user.Identity;
        if (identity?.IsAuthenticated != true) return CallerIdentity.Anonymous;

        if (string.Equals(identity.AuthenticationType, ApiKeyAuthenticationType, StringComparison.Ordinal)
            && Value(user, CallerIdentity.ApiKeyClaim) is { } key)
            return $"apikey:{key}";

        var app = Value(user, "azp") ?? Value(user, "appid") ?? Value(user, "sub") ?? Value(user, ObjectIdClaim) ?? Value(user, MappedObjectIdClaim);
        if ((Value(user, CallerIdentity.TenantIdClaim) ?? Value(user, CallerIdentity.MappedTenantIdClaim)) is { } tid)
        {
            if (app is null) throw new A2AAuthException(401, "Caller identity has a tenant but no application or subject id.");
            return $"tenant:{tid}/app:{app}";
        }

        if ((app ?? Value(user, ClaimTypes.NameIdentifier) ?? identity.Name) is { Length: > 0 } sub) return $"user:{sub}";
        // Never fall back to a shared key: that would let unrelated callers see each other's tasks.
        throw new A2AAuthException(401, "Caller identity has no subject.");
    }

    /// <summary>True only for <see cref="TaskState.AuthRequired"/> when both owners are bearer owners of the same tenant.</summary>
    /// <param name="taskOwner">The owner key of the task.</param>
    /// <param name="callerOwner">The owner key of the caller.</param>
    /// <param name="state">The state of the task.</param>
    /// <returns><see langword="true"/> when the caller may continue the task.</returns>
    public bool CanContinueInterrupted(string taskOwner, string callerOwner, TaskState state)
    {
        ArgumentNullException.ThrowIfNull(taskOwner);
        ArgumentNullException.ThrowIfNull(callerOwner);
        if (state != TaskState.AuthRequired) return false;
        return TenantPrefix(taskOwner) is { } t && t == TenantPrefix(callerOwner);
    }

    /// <summary><c>tenant:{tid}</c> of a bearer owner key, else null.</summary>
    private static string? TenantPrefix(string owner)
    {
        if (!owner.StartsWith("tenant:", StringComparison.Ordinal)) return null;
        var slash = owner.IndexOf("/app:", StringComparison.Ordinal);
        return slash > "tenant:".Length ? owner[..slash] : null;
    }

    private static string? Value(ClaimsPrincipal user, string type)
        => user.FindFirst(type)?.Value is { Length: > 0 } v ? v : null;
}
