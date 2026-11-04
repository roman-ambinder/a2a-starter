using Microsoft.Extensions.Logging;

namespace Ambinder.A2A.Server.Push;

/// <summary>Log messages of push delivery. EventIds are public API: 1101 to 1199 are fixed (the Server block is 1000 to 1999).</summary>
internal static partial class Log
{
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
