// Owned by package 07; minimal version added by 05.
namespace Ambinder.A2A.Server.Security;

/// <summary>
/// Says which owners may continue a task that is waiting in <c>AUTH_REQUIRED</c>, and until when.
/// </summary>
public sealed record TaskContinuationGrant
{
    /// <summary>The owners allowed to continue the task. Matched exactly.</summary>
    public required IReadOnlyList<OwnerKey> AllowedOwners { get; init; }

    /// <summary>When the grant stops being valid.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
