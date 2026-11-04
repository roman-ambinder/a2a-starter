namespace Ambinder.A2A.Protocol;

/// <summary>
/// An authentication or authorization failure (HTTP 401 or 403). This is not an A2A error code.
/// Shared by server and client.
/// </summary>
/// <param name="httpStatus">The HTTP status: 401 (unauthenticated) or 403 (forbidden).</param>
/// <param name="message">The message that describes the failure.</param>
public class A2AAuthException(int httpStatus, string message) : Exception(message)
{
    /// <summary>HTTP status: 401 (unauthenticated) or 403 (forbidden).</summary>
    public int HttpStatus { get; } = httpStatus;
}
