using Microsoft.Extensions.Logging;

namespace Ambinder.A2A.Server;

/// <summary>
/// Log messages of <c>Ambinder.A2A.Server</c>. EventIds are public API and fixed: tasks 1001 to 1099, push delivery 1101 to 1199
/// (the Server block is 1000 to 1999).
/// </summary>
/// <remarks>
/// Keep every <c>[LoggerMessage]</c> method of this assembly in this one class and this one file. The logging source generator
/// writes separate declarations (other classes, or other files of the same partial class) in an order that changes from build
/// to build, and then two builds of the same commit give different DLLs (the <c>reproducibility</c> CI job).
/// </remarks>
internal static partial class Log
{
    // Tasks (1001 to 1099).
    [LoggerMessage(EventId = 1001, EventName = nameof(AgentCancelFailed), Level = LogLevel.Warning,
        Message = "Agent {Agent} CancelAsync failed for task {TaskId}; canceling anyway")]
    public static partial void AgentCancelFailed(ILogger logger, Exception exception, string agent, string taskId);

    [LoggerMessage(EventId = 1002, EventName = nameof(AgentEndedWithoutFinalState), Level = LogLevel.Warning,
        Message = "Agent {Agent} ended task {TaskId} without a final state; failing it")]
    public static partial void AgentEndedWithoutFinalState(ILogger logger, string agent, string taskId);

    [LoggerMessage(EventId = 1003, EventName = nameof(AgentStoppedAfterCancel), Level = LogLevel.Debug,
        Message = "Agent {Agent} stopped after task {TaskId} was canceled")]
    public static partial void AgentStoppedAfterCancel(ILogger logger, Exception exception, string agent, string taskId);

    [LoggerMessage(EventId = 1004, EventName = nameof(AgentThrewAfterFinalState), Level = LogLevel.Warning,
        Message = "Agent {Agent} threw after task {TaskId} reached a final state")]
    public static partial void AgentThrewAfterFinalState(ILogger logger, Exception exception, string agent, string taskId);

    [LoggerMessage(EventId = 1005, EventName = nameof(AgentFailed), Level = LogLevel.Error,
        Message = "Agent {Agent} failed on task {TaskId}")]
    public static partial void AgentFailed(ILogger logger, Exception exception, string agent, string taskId);

    [LoggerMessage(EventId = 1006, EventName = nameof(CouldNotFailTask), Level = LogLevel.Error,
        Message = "Could not fail task {TaskId}")]
    public static partial void CouldNotFailTask(ILogger logger, Exception exception, string taskId);

    [LoggerMessage(EventId = 1011, EventName = nameof(RecordingTelemetryFailed), Level = LogLevel.Debug,
        Message = "Recording telemetry failed for task {TaskId}")]
    public static partial void RecordingTelemetryFailed(ILogger logger, Exception exception, string taskId);

    [LoggerMessage(EventId = 1012, EventName = nameof(TaskEventObserverFailed), Level = LogLevel.Error,
        Message = "Task event observer {Observer} failed for task {TaskId}")]
    public static partial void TaskEventObserverFailed(ILogger logger, Exception exception, string observer, string taskId);

    // Push delivery (1101 to 1199).
    [LoggerMessage(EventId = 1101, EventName = nameof(PushQueueFull), Level = LogLevel.Warning,
        Message = "Push queue is full; dropped an event of task {TaskId}")]
    public static partial void PushQueueFull(ILogger logger, string taskId);

    [LoggerMessage(EventId = 1102, EventName = nameof(PushDeliveryFailedUnexpectedly), Level = LogLevel.Error,
        Message = "Push delivery failed unexpectedly for task {TaskId}")]
    public static partial void PushDeliveryFailedUnexpectedly(ILogger logger, Exception exception, string taskId);

    [LoggerMessage(EventId = 1103, EventName = nameof(PushRejected), Level = LogLevel.Warning,
        Message = "Push to {Url} for task {TaskId} was rejected ({Failure}); not retrying")]
    public static partial void PushRejected(ILogger logger, string url, string taskId, string failure);

    [LoggerMessage(EventId = 1104, EventName = nameof(PushGaveUp), Level = LogLevel.Warning,
        Message = "Push to {Url} for task {TaskId} failed after {Attempts} attempts ({Failure}); giving up")]
    public static partial void PushGaveUp(ILogger logger, string url, string taskId, int attempts, string failure);

    [LoggerMessage(EventId = 1105, EventName = nameof(PushRetrying), Level = LogLevel.Information,
        Message = "Push to {Url} for task {TaskId} failed ({Failure}); retry {Attempt} of {Retries} in {Delay}")]
    public static partial void PushRetrying(ILogger logger, string url, string taskId, string failure, int attempt, int retries, TimeSpan delay);
}
