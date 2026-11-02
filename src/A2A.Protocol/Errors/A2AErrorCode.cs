namespace A2A.Protocol;

/// <summary>A2A error codes. The underlying value is the JSON-RPC error code.</summary>
public enum A2AErrorCode
{
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603,
    TaskNotFound = -32001,
    TaskNotCancelable = -32002,
    PushNotificationNotSupported = -32003,
    UnsupportedOperation = -32004,
    ContentTypeNotSupported = -32005,
    InvalidAgentResponse = -32006,
    ExtendedAgentCardNotConfigured = -32007,
    ExtensionSupportRequired = -32008,
    VersionNotSupported = -32009,
}
