namespace A2A.Protocol;

/// <summary>Protocol-wide constants: version, header names, content types and binding names.</summary>
public static class A2AConstants
{
    public const string ProtocolVersion = "1.0";
    public const string VersionHeader = "A2A-Version";
    public const string ExtensionsHeader = "A2A-Extensions";
    public const string GrpcVersionKey = "a2a-version";
    public const string GrpcExtensionsKey = "a2a-extensions";
    /// <summary>Media type of HTTP+JSON (REST) bodies and push deliveries (spec 11.1, 14.1).</summary>
    public const string ContentType = "application/a2a+json";
    /// <summary>Media type of JSON-RPC requests and responses (spec 9.1).</summary>
    public const string JsonRpcContentType = "application/json";
    public const string EventStream = "text/event-stream";
    public const string BindingJsonRpc = "JSONRPC";
    public const string BindingRest = "HTTP+JSON";
    public const string BindingGrpc = "GRPC";
    public const string WellKnownCardPath = "/.well-known/agent-card.json";
    public const string NotificationTokenHeader = "X-A2A-Notification-Token";
    public const string PushSignatureHeader = "X-A2A-Push-Signature";
}
