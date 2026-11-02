using A2A.Protocol;
using Google.Rpc;
using Grpc.Core;

namespace A2A.Client.Transports;

/// <summary>Maps a failed gRPC call back to the exception the caller expects.</summary>
internal static class GrpcErrorTranslator
{
    /// <summary>
    /// UNAUTHENTICATED / PERMISSION_DENIED → <see cref="A2AAuthException"/> (401 / 403). A <c>google.rpc.Status</c> with an
    /// ErrorInfo reason → <see cref="A2AException"/> with the code from <see cref="A2AError.FromReason"/> and the ErrorInfo
    /// metadata as <see cref="A2AException.Data"/>. Without a reason: INVALID_ARGUMENT → InvalidParams, UNIMPLEMENTED →
    /// MethodNotFound, anything else → InternalError; the message and the gRPC status are kept.
    /// </summary>
    public static Exception Translate(RpcException ex)
    {
        switch (ex.StatusCode)
        {
            case StatusCode.Unauthenticated:
                return new A2AAuthException(401, Message(ex));
            case StatusCode.PermissionDenied:
                return new A2AAuthException(403, Message(ex));
        }

        var status = ex.GetRpcStatus();
        if (status is not null)
        {
            foreach (var detail in status.Details)
            {
                if (!detail.Is(ErrorInfo.Descriptor)) continue;
                var info = detail.Unpack<ErrorInfo>();
                if (A2AError.FromReason(info.Reason) is { } code)
                    return new A2AException(code, Message(ex), ClientSupport.MetadataToData(info.Metadata));
            }
        }

        var fallback = ex.StatusCode switch
        {
            StatusCode.InvalidArgument => A2AErrorCode.InvalidParams,
            StatusCode.Unimplemented => A2AErrorCode.MethodNotFound,
            _ => A2AErrorCode.InternalError,
        };
        return new A2AException(fallback, $"{Message(ex)} (gRPC {ex.StatusCode})");
    }

    private static string Message(RpcException ex)
    {
        var status = ex.GetRpcStatus();
        return !string.IsNullOrEmpty(status?.Message) ? status.Message : ex.Status.Detail;
    }
}
