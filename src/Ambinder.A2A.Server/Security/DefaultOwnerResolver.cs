using System.Security.Claims;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;

namespace Ambinder.A2A.Server.Security;

/// <summary>
/// Default task ownership for Entra-shaped tokens: one app, or one user, in one tenant. The rules, in order:
/// <list type="number">
/// <item><description>Not authenticated: <see cref="CallerKind.Anonymous"/>, owner <c>anonymous</c>.</description></item>
/// <item><description>A tenant (<c>tid</c>) and a scope (<c>scp</c>): <see cref="CallerKind.User"/>, owner <c>tid:{tid}/user:{oid}</c>.</description></item>
/// <item><description>A tenant and no scope: <see cref="CallerKind.Application"/>, owner <c>tid:{tid}/app:{appid}</c> (<c>azp</c>, else <c>appid</c>).</description></item>
/// <item><description>Anything else: <see cref="A2AAuthException"/> (401). There is no shared fallback key.</description></item>
/// </list>
/// Other authentication schemes need their own <see cref="IOwnerResolver"/>, which can decorate this class.
/// </summary>
/// <remarks>The resolver has no state and is thread-safe.</remarks>
public sealed class DefaultOwnerResolver : IOwnerResolver
{
    /// <inheritdoc/>
    public ValueTask<A2ACaller> ResolveAsync(ClaimsPrincipal user, AgentRegistration agent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(agent);

        var identity = user.Identity;
        if (identity?.IsAuthenticated != true)
        {
            return ValueTask.FromResult(new A2ACaller { Owner = "anonymous", Kind = CallerKind.Anonymous, Principal = OwnerClaims.Snapshot(user) });
        }

        if (OwnerClaims.TenantId(user) is not { } tid)
        {
            throw new A2AAuthException(401,
                $"The caller was authenticated by scheme '{identity.AuthenticationType}', which the default owner resolver does not understand. "
                + "Register an IOwnerResolver for this scheme (see docs: Security > Other schemes).");
        }

        var appId = OwnerClaims.AppId(user);
        if (OwnerClaims.IsDelegated(user))
        {
            var oid = OwnerClaims.ObjectId(user)
                ?? throw new A2AAuthException(401, "Delegated token has no oid claim.");
            return ValueTask.FromResult(new A2ACaller
            {
                Owner = OwnerKey.ForUser(tid, oid).Value,
                Kind = CallerKind.User,
                EntraTenantId = tid,
                AppId = appId,
                UserObjectId = oid,
                Principal = OwnerClaims.Snapshot(user),
            });
        }

        if (appId is null)
        {
            throw new A2AAuthException(401, "App-only token has no azp or appid claim.");
        }

        return ValueTask.FromResult(new A2ACaller
        {
            Owner = OwnerKey.ForApp(tid, appId).Value,
            Kind = CallerKind.Application,
            EntraTenantId = tid,
            AppId = appId,
            Principal = OwnerClaims.Snapshot(user),
        });
    }
}
