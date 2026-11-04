namespace Ambinder.A2A.Protocol;

/// <summary>Tells an authentication failure from an authorization failure.</summary>
/// <remarks>More values can be added in later versions; handle unknown values.</remarks>
public enum A2AAuthFailureKind
{
    /// <summary>The caller is not authenticated (HTTP 401, gRPC <c>UNAUTHENTICATED</c>).</summary>
    Unauthenticated,

    /// <summary>The caller is authenticated but not allowed (HTTP 403, gRPC <c>PERMISSION_DENIED</c>).</summary>
    Forbidden,
}
