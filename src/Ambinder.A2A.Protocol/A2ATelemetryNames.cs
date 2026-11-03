namespace Ambinder.A2A.Protocol;

/// <summary>
/// Names of the spans, attributes, metrics and events the A2A libraries emit through <c>System.Diagnostics</c>.
/// The libraries reference no OpenTelemetry package; a host adds the sources and the meter to its own exporter.
/// </summary>
public static class A2ATelemetryNames
{
    // Sources and meter.
    public const string ServerSource = "A2A.Server";
    public const string ServerMeter = "A2A.Server";
    public const string ClientSource = "A2A.Client";
    public const string ProtocolMeter = "A2A.Protocol";

    // Attributes.
    public const string Method = "a2a.method";
    public const string Agent = "a2a.agent";
    public const string Transport = "a2a.transport";
    public const string TaskId = "a2a.task.id";
    public const string ContextId = "a2a.context.id";
    public const string Tenant = "a2a.tenant";
    public const string TaskState = "a2a.task.state";
    public const string ErrorType = "error.type";
    public const string PushAttempt = "a2a.push.attempt";
    public const string Outcome = "outcome";
    public const string ServerAddress = "server.address";

    // Values of the transport attribute.
    public const string TransportJsonRpc = "jsonrpc";
    public const string TransportRest = "rest";
    public const string TransportGrpc = "grpc";

    // Metrics.
    public const string TasksCompleted = "a2a.server.tasks.completed";
    public const string StreamsActive = "a2a.server.streams.active";
    public const string PushDeliveries = "a2a.server.push.deliveries";

    // Span event.
    public const string TaskStateEvent = "a2a.task.state";

    /// <summary>The attribute value for a task state, for example <c>input-required</c>.</summary>
    public static string StateValue(Ambinder.A2A.Protocol.TaskState state) => state switch
    {
        Ambinder.A2A.Protocol.TaskState.Submitted => "submitted",
        Ambinder.A2A.Protocol.TaskState.Working => "working",
        Ambinder.A2A.Protocol.TaskState.Completed => "completed",
        Ambinder.A2A.Protocol.TaskState.Failed => "failed",
        Ambinder.A2A.Protocol.TaskState.Canceled => "canceled",
        Ambinder.A2A.Protocol.TaskState.InputRequired => "input-required",
        Ambinder.A2A.Protocol.TaskState.Rejected => "rejected",
        Ambinder.A2A.Protocol.TaskState.AuthRequired => "auth-required",
        _ => "unspecified",
    };
}
