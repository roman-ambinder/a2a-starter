namespace Ambinder.A2A.Protocol;

/// <summary>One row of the A2A error catalog.</summary>
/// <param name="Code">The A2A error code (JSON-RPC code).</param>
/// <param name="HttpStatus">HTTP status used by the REST binding.</param>
/// <param name="GrpcStatus">Name of the <c>Grpc.Core.StatusCode</c> member, for example "NotFound". Kept as a string so this project does not reference gRPC.</param>
/// <param name="Reason">ErrorInfo reason, or null for the standard JSON-RPC errors.</param>
public sealed record A2AErrorInfo(A2AErrorCode Code, int HttpStatus, string GrpcStatus, string? Reason);

/// <summary>The A2A error catalog: maps each error code to its HTTP, gRPC and ErrorInfo values.</summary>
public static class A2AError
{
    /// <summary>Domain used in ErrorInfo details.</summary>
    public const string Domain = "a2a-protocol.org";

    private static readonly IReadOnlyDictionary<A2AErrorCode, A2AErrorInfo> Catalog = new Dictionary<A2AErrorCode, A2AErrorInfo>
    {
        [A2AErrorCode.ParseError] = new(A2AErrorCode.ParseError, 400, "InvalidArgument", null),
        [A2AErrorCode.InvalidRequest] = new(A2AErrorCode.InvalidRequest, 400, "InvalidArgument", null),
        [A2AErrorCode.MethodNotFound] = new(A2AErrorCode.MethodNotFound, 404, "Unimplemented", null),
        [A2AErrorCode.InvalidParams] = new(A2AErrorCode.InvalidParams, 400, "InvalidArgument", null),
        [A2AErrorCode.InternalError] = new(A2AErrorCode.InternalError, 500, "Internal", null),
        [A2AErrorCode.TaskNotFound] = new(A2AErrorCode.TaskNotFound, 404, "NotFound", "TASK_NOT_FOUND"),
        [A2AErrorCode.TaskNotCancelable] = new(A2AErrorCode.TaskNotCancelable, 400, "FailedPrecondition", "TASK_NOT_CANCELABLE"),
        [A2AErrorCode.PushNotificationNotSupported] = new(A2AErrorCode.PushNotificationNotSupported, 400, "FailedPrecondition", "PUSH_NOTIFICATION_NOT_SUPPORTED"),
        [A2AErrorCode.UnsupportedOperation] = new(A2AErrorCode.UnsupportedOperation, 400, "FailedPrecondition", "UNSUPPORTED_OPERATION"),
        [A2AErrorCode.ContentTypeNotSupported] = new(A2AErrorCode.ContentTypeNotSupported, 400, "InvalidArgument", "CONTENT_TYPE_NOT_SUPPORTED"),
        [A2AErrorCode.InvalidAgentResponse] = new(A2AErrorCode.InvalidAgentResponse, 500, "Internal", "INVALID_AGENT_RESPONSE"),
        [A2AErrorCode.ExtendedAgentCardNotConfigured] = new(A2AErrorCode.ExtendedAgentCardNotConfigured, 400, "FailedPrecondition", "EXTENDED_AGENT_CARD_NOT_CONFIGURED"),
        [A2AErrorCode.ExtensionSupportRequired] = new(A2AErrorCode.ExtensionSupportRequired, 400, "FailedPrecondition", "EXTENSION_SUPPORT_REQUIRED"),
        [A2AErrorCode.VersionNotSupported] = new(A2AErrorCode.VersionNotSupported, 400, "FailedPrecondition", "VERSION_NOT_SUPPORTED"),
    };

    private static readonly Dictionary<string, A2AErrorCode> ByReason = Catalog.Values
        .Where(info => info.Reason is not null)
        .ToDictionary(info => info.Reason!, info => info.Code, StringComparer.Ordinal);

    /// <summary>Returns the catalog row for an error code.</summary>
    /// <param name="code">The error code.</param>
    /// <returns>The row with the HTTP status, gRPC status and ErrorInfo reason.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="code"/> is not a known A2A error code.</exception>
    public static A2AErrorInfo Get(A2AErrorCode code)
        => Catalog.TryGetValue(code, out var info)
            ? info
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown A2A error code.");

    /// <summary>Returns the error code for an ErrorInfo reason, or null if the reason is unknown. Matching is case-sensitive.</summary>
    /// <param name="reason">The ErrorInfo reason, for example <c>TASK_NOT_FOUND</c>.</param>
    /// <returns>The error code, or <see langword="null"/> when the reason is unknown.</returns>
    public static A2AErrorCode? FromReason(string reason)
        => ByReason.TryGetValue(reason, out var code) ? code : null;
}
