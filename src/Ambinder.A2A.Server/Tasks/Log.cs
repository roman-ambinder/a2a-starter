using Microsoft.Extensions.Logging;

namespace Ambinder.A2A.Server.Tasks;

/// <summary>Log messages of the task area. EventIds are public API: 1001 to 1099 are fixed (the Server block is 1000 to 1999).</summary>
internal static partial class Log
{
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
}
