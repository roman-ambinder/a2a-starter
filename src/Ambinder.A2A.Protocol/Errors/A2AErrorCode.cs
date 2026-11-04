namespace Ambinder.A2A.Protocol;

/// <summary>A2A error codes. The underlying value is the JSON-RPC error code.</summary>
/// <remarks>More values can be added in later versions; handle unknown values. Do not rely on the numeric values outside the wire format.</remarks>
public enum A2AErrorCode
{
    /// <summary>The request is not valid JSON.</summary>
    ParseError = -32700,
    /// <summary>The request is not a valid request object.</summary>
    InvalidRequest = -32600,
    /// <summary>The method does not exist or is not available.</summary>
    MethodNotFound = -32601,
    /// <summary>The method parameters are not valid.</summary>
    InvalidParams = -32602,
    /// <summary>An internal error occurred.</summary>
    InternalError = -32603,
    /// <summary>The task does not exist, or the caller may not see it.</summary>
    TaskNotFound = -32001,
    /// <summary>The task is in a state that cannot be canceled.</summary>
    TaskNotCancelable = -32002,
    /// <summary>The agent does not support push notifications.</summary>
    PushNotificationNotSupported = -32003,
    /// <summary>The agent does not support this operation.</summary>
    UnsupportedOperation = -32004,
    /// <summary>The agent does not support the content type of a part.</summary>
    ContentTypeNotSupported = -32005,
    /// <summary>The agent returned a response that does not match the protocol.</summary>
    InvalidAgentResponse = -32006,
    /// <summary>The agent has no extended agent card.</summary>
    ExtendedAgentCardNotConfigured = -32007,
    /// <summary>The request needs an extension that the other side did not declare.</summary>
    ExtensionSupportRequired = -32008,
    /// <summary>The protocol version of the request is not supported.</summary>
    VersionNotSupported = -32009,
}
