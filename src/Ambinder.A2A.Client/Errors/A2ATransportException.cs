using System.Globalization;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client;

/// <summary>A call failed at the transport level, not with an A2A protocol error.</summary>
public class A2ATransportException : A2AException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="isTransient">Whether a later retry can succeed.</param>
    /// <param name="httpStatus">The HTTP status, or <see langword="null"/> when there was none.</param>
    /// <param name="grpcStatus">The gRPC status name, or <see langword="null"/> when there was none.</param>
    public A2ATransportException(string message, bool isTransient, int? httpStatus = null, string? grpcStatus = null)
        : base(A2AErrorCode.InternalError, message)
    {
        IsTransient = isTransient;
        HttpStatus = httpStatus;
        GrpcStatus = grpcStatus;
    }

    /// <summary>Whether a later retry can succeed. True for HTTP 5xx, 408, 429 and network errors.</summary>
    public bool IsTransient { get; }

    /// <summary>The HTTP status, or <see langword="null"/> when there was none.</summary>
    public int? HttpStatus { get; }

    /// <summary>The gRPC status name, or <see langword="null"/> when there was none.</summary>
    public string? GrpcStatus { get; }

    /// <summary>Maps a non-A2A HTTP failure. 408, 429 and 5xx are transient; other statuses are permanent.</summary>
    /// <param name="status">The HTTP status.</param>
    /// <param name="agent">The client or agent name for the message.</param>
    /// <param name="method">The A2A method for the message.</param>
    /// <returns>The exception.</returns>
    public static A2ATransportException FromHttpStatus(int status, string agent, string method)
        => new(string.Create(CultureInfo.InvariantCulture, $"{method} to agent '{agent}' failed with HTTP {status}."),
            status is 408 or 429 or >= 500, httpStatus: status);
}
