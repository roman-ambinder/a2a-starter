namespace Ambinder.A2A.Protocol;

/// <summary>
/// An authentication or authorization failure (HTTP 401 or 403, or the matching gRPC status). This is not an A2A error code.
/// Shared by server and client.
/// </summary>
public class A2AAuthException : Exception
{
    /// <summary>Creates the exception from an HTTP status. 401 is <see cref="A2AAuthFailureKind.Unauthenticated"/>, any other status is <see cref="A2AAuthFailureKind.Forbidden"/>.</summary>
    /// <param name="httpStatus">The HTTP status: 401 (unauthenticated) or 403 (forbidden).</param>
    /// <param name="message">The message that describes the failure.</param>
    public A2AAuthException(int httpStatus, string message)
        : this(httpStatus == 401 ? A2AAuthFailureKind.Unauthenticated : A2AAuthFailureKind.Forbidden, message, httpStatus)
    {
    }

    /// <summary>Creates the exception with all details.</summary>
    /// <param name="kind">Whether the caller is unauthenticated or forbidden.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="httpStatus">The HTTP status, or <see langword="null"/> when the failure did not come over HTTP (for example gRPC).</param>
    /// <param name="grpcStatus">The gRPC status name (for example <c>PERMISSION_DENIED</c>), or <see langword="null"/>.</param>
    /// <param name="error">The OAuth <c>error</c> value from a <c>WWW-Authenticate</c> challenge, or <see langword="null"/>.</param>
    /// <param name="errorDescription">The OAuth <c>error_description</c> value from a challenge, or <see langword="null"/>.</param>
    public A2AAuthException(A2AAuthFailureKind kind, string message, int? httpStatus = null, string? grpcStatus = null,
        string? error = null, string? errorDescription = null)
        : base(message)
    {
        Kind = kind;
        HttpStatus = httpStatus;
        GrpcStatus = grpcStatus;
        Error = error;
        ErrorDescription = errorDescription;
    }

    /// <summary>Whether the caller is unauthenticated or forbidden.</summary>
    public A2AAuthFailureKind Kind { get; }

    /// <summary>HTTP status (401 or 403), or <see langword="null"/> when the failure did not come over HTTP.</summary>
    public int? HttpStatus { get; }

    /// <summary>The gRPC status name, or <see langword="null"/> when the failure did not come over gRPC.</summary>
    public string? GrpcStatus { get; }

    /// <summary>The OAuth <c>error</c> value from the challenge, or <see langword="null"/>.</summary>
    public string? Error { get; }

    /// <summary>The OAuth <c>error_description</c> value from the challenge, or <see langword="null"/>.</summary>
    public string? ErrorDescription { get; }
}
