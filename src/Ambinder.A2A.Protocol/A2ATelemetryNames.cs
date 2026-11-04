namespace Ambinder.A2A.Protocol;

/// <summary>
/// Names of the spans, attributes, metrics and events the A2A libraries emit through <c>System.Diagnostics</c>.
/// The libraries reference no OpenTelemetry package; a host adds the sources and the meter to its own exporter.
/// </summary>
public static class A2ATelemetryNames
{
    // Sources and meter.
    /// <summary>Name of the <c>ActivitySource</c> the server library uses for spans.</summary>
    public const string ServerSource = "Ambinder.A2A.Server";
    /// <summary>Name of the <c>Meter</c> the server library uses for metrics.</summary>
    public const string ServerMeter = "Ambinder.A2A.Server";
    /// <summary>Name of the <c>ActivitySource</c> the client library uses for spans.</summary>
    public const string ClientSource = "Ambinder.A2A.Client";
    /// <summary>Name of the <c>Meter</c> the protocol library uses for metrics.</summary>
    public const string ProtocolMeter = "Ambinder.A2A.Protocol";

    // Attributes.
    /// <summary>Attribute: the A2A method name, for example <c>SendMessage</c>.</summary>
    public const string Method = "a2a.method";
    /// <summary>Attribute: the name of the agent that handles the call.</summary>
    public const string Agent = "a2a.agent";
    /// <summary>Attribute: the transport of the call. The values are <see cref="TransportJsonRpc"/>, <see cref="TransportRest"/> and <see cref="TransportGrpc"/>.</summary>
    public const string Transport = "a2a.transport";
    /// <summary>Attribute: the task ID.</summary>
    public const string TaskId = "a2a.task.id";
    /// <summary>Attribute: the context ID.</summary>
    public const string ContextId = "a2a.context.id";
    /// <summary>Attribute: the tenant of the call.</summary>
    public const string Tenant = "a2a.tenant";
    /// <summary>Attribute: the task state, as returned by <see cref="StateValue(TaskState)"/>.</summary>
    public const string TaskState = "a2a.task.state";
    /// <summary>Attribute: the error type of a failed call.</summary>
    public const string ErrorType = "error.type";
    /// <summary>Attribute: the number of the push delivery attempt, starting at 1.</summary>
    public const string PushAttempt = "a2a.push.attempt";
    /// <summary>Attribute: the outcome of an operation, for example a push delivery.</summary>
    public const string Outcome = "outcome";
    /// <summary>Attribute: the host name of the remote server.</summary>
    public const string ServerAddress = "server.address";

    // Values of the transport attribute.
    /// <summary>Value of the transport attribute for JSON-RPC calls.</summary>
    public const string TransportJsonRpc = "jsonrpc";
    /// <summary>Value of the transport attribute for REST calls.</summary>
    public const string TransportRest = "rest";
    /// <summary>Value of the transport attribute for gRPC calls.</summary>
    public const string TransportGrpc = "grpc";

    // Metrics.
    /// <summary>Metric: count of tasks that reached a final state.</summary>
    public const string TasksCompleted = "a2a.server.tasks.completed";
    /// <summary>Metric: number of streams that are open now.</summary>
    public const string StreamsActive = "a2a.server.streams.active";
    /// <summary>Metric: count of push notification deliveries.</summary>
    public const string PushDeliveries = "a2a.server.push.deliveries";

    // Span event.
    /// <summary>Name of the span event that is added when a task changes state.</summary>
    public const string TaskStateEvent = "a2a.task.state";

    /// <summary>The attribute value for a task state, for example <c>input-required</c>.</summary>
    /// <param name="state">The task state.</param>
    /// <returns>The lower-case value with dashes. <see cref="Ambinder.A2A.Protocol.TaskState.Unspecified"/> and unknown states give <c>unspecified</c>.</returns>
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
