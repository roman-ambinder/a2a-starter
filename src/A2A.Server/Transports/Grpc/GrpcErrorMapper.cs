using A2A.Protocol;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using GrpcStatus = global::Grpc.Core.Status;
using Microsoft.Extensions.Logging;

namespace A2A.Server.Transports.Grpc;

/// <summary>
/// A2A errors → <see cref="RpcException"/> carrying a <c>google.rpc.Status</c> (via Grpc.StatusProto). Status code comes from the
/// error catalog; errors with a reason also get an <c>ErrorInfo{reason, domain=a2a-protocol.org, metadata}</c> detail, where
/// metadata is <see cref="A2AException.Data"/> flattened to strings exactly as the REST binding does.
/// </summary>
public static class GrpcErrorMapper
{
    /// <summary>Catalog status plus, when the catalog has a reason, an ErrorInfo detail. JSON-RPC-only errors carry status and message only.</summary>
    public static RpcException ToRpcException(A2AException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var info = A2AError.Get(error.Code);
        var status = new Google.Rpc.Status { Code = (int)System.Enum.Parse<StatusCode>(info.GrpcStatus), Message = error.Message };
        if (TransportErrors.ReasonFor(error) is { } reason)
        {
            var errorInfo = new ErrorInfo { Reason = reason, Domain = A2AError.Domain };
            foreach (var (name, value) in TransportErrors.ErrorMetadata(error.Data)) errorInfo.Metadata[name] = value;
            status.Details.Add(Any.Pack(errorInfo));
        }
        return status.ToRpcException();
    }

    /// <summary>401 → <c>UNAUTHENTICATED</c>, anything else (403) → <c>PERMISSION_DENIED</c>. No details.</summary>
    public static RpcException ToRpcException(A2AAuthException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var code = error.HttpStatus == 401 ? StatusCode.Unauthenticated : StatusCode.PermissionDenied;
        return new RpcException(new GrpcStatus(code, error.Message));
    }

    /// <summary>The <c>NOT_FOUND</c> answer for a connection on a port no agent owns.</summary>
    public static RpcException UnknownAgent(int port)
        => new(new GrpcStatus(StatusCode.NotFound, $"No agent listens for gRPC on port {port}."));

    /// <summary>
    /// Any exception → <see cref="RpcException"/>. <see cref="RpcException"/> passes through; unknown exceptions are logged and
    /// become <c>INTERNAL</c> with a generic message; cancellation by the caller becomes <c>CANCELLED</c>.
    /// </summary>
    internal static RpcException Map(Exception ex, ILogger logger, CancellationToken ct) => ex switch
    {
        RpcException rpc => rpc,
        A2AAuthException auth => ToRpcException(auth),
        OperationCanceledException when ct.IsCancellationRequested => new RpcException(new GrpcStatus(StatusCode.Cancelled, "The call was cancelled.")),
        _ => ToRpcException(TransportErrors.ToA2AException(ex, logger)),
    };
}
