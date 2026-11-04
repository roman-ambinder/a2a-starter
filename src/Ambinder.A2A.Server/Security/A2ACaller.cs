using System.Security.Claims;
using System.Text;

namespace Ambinder.A2A.Server.Security;

/// <summary>
/// Who called: the owner key, the kind of caller and the Entra identity behind it. The agent gets one for every run, including
/// runs that outlive the HTTP request.
/// </summary>
/// <remarks>
/// <see cref="ToString"/> never shows <see cref="AccessToken"/>. Build instances with an object initializer.
/// </remarks>
public sealed record A2ACaller
{
    /// <summary>The opaque owner key, at most 256 characters. See <see cref="OwnerKey"/>.</summary>
    public required string Owner { get; init; }

    /// <summary>The kind of caller.</summary>
    public required CallerKind Kind { get; init; }

    /// <summary>The Entra tenant id (<c>tid</c> claim), or <see langword="null"/>.</summary>
    public string? EntraTenantId { get; init; }

    /// <summary>The app id (<c>azp</c> in v2 tokens, <c>appid</c> in v1). For a <see cref="CallerKind.User"/> it is the client app the user signed in through.</summary>
    public string? AppId { get; init; }

    /// <summary>The user object id (<c>oid</c>). Only set for <see cref="CallerKind.User"/>.</summary>
    public string? UserObjectId { get; init; }

    /// <summary>A snapshot copy of the caller's principal. It is safe to use after the request ends.</summary>
    public required ClaimsPrincipal Principal { get; init; }

    /// <summary>
    /// The raw bearer token. Only set when <c>A2ASecurityOptions.KeepCallerToken</c> is <see langword="true"/>. It is never stored,
    /// logged or printed.
    /// </summary>
    public string? AccessToken { get; init; }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Owner = ").Append(Owner)
            .Append(", Kind = ").Append(Kind)
            .Append(", EntraTenantId = ").Append(EntraTenantId)
            .Append(", AppId = ").Append(AppId)
            .Append(", UserObjectId = ").Append(UserObjectId)
            .Append(", AccessToken = ").Append(AccessToken is null ? "null" : "***");
        return true;
    }
}
