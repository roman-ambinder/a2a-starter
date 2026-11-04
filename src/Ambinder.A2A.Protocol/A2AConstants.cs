namespace Ambinder.A2A.Protocol;

/// <summary>Protocol-wide constants: version, header names, content types and binding names.</summary>
public static class A2AConstants
{
    /// <summary>The A2A protocol version this SDK speaks. Value: <c>1.0</c>.</summary>
    public const string ProtocolVersion = "1.0";
    /// <summary>Name of the HTTP header that carries the protocol version. Value: <c>A2A-Version</c>.</summary>
    public const string VersionHeader = "A2A-Version";
    /// <summary>Name of the HTTP header that lists the extension URIs a request uses. Value: <c>A2A-Extensions</c>.</summary>
    public const string ExtensionsHeader = "A2A-Extensions";
    /// <summary>Name of the gRPC metadata key that carries the protocol version. Value: <c>a2a-version</c>.</summary>
    public const string GrpcVersionKey = "a2a-version";
    /// <summary>Name of the gRPC metadata key that lists the extension URIs a request uses. Value: <c>a2a-extensions</c>.</summary>
    public const string GrpcExtensionsKey = "a2a-extensions";
    /// <summary>Media type of HTTP+JSON (REST) bodies and push deliveries (spec 11.1, 14.1).</summary>
    public const string ContentType = "application/a2a+json";
    /// <summary>Media type of JSON-RPC requests and responses (spec 9.1).</summary>
    public const string JsonRpcContentType = "application/json";
    /// <summary>Media type of server-sent event streams. Value: <c>text/event-stream</c>.</summary>
    public const string EventStream = "text/event-stream";
    /// <summary>Protocol binding name for JSON-RPC over HTTP. Value: <c>JSONRPC</c>.</summary>
    public const string BindingJsonRpc = "JSONRPC";
    /// <summary>Protocol binding name for HTTP+JSON (REST). Value: <c>HTTP+JSON</c>.</summary>
    public const string BindingRest = "HTTP+JSON";
    /// <summary>Protocol binding name for gRPC. Value: <c>GRPC</c>.</summary>
    public const string BindingGrpc = "GRPC";
    /// <summary>Path of the public agent card on a host. Value: <c>/.well-known/agent-card.json</c>.</summary>
    public const string WellKnownCardPath = "/.well-known/agent-card.json";
    /// <summary>Name of the header that carries the push notification token on a webhook call. Value: <c>X-A2A-Notification-Token</c>.</summary>
    public const string NotificationTokenHeader = "X-A2A-Notification-Token";
    /// <summary>Name of the header that carries the signature of a push notification. Value: <c>X-A2A-Push-Signature</c>.</summary>
    public const string PushSignatureHeader = "X-A2A-Push-Signature";
}
