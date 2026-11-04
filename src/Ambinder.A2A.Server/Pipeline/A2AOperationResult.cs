using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server;

/// <summary>The result of an operation: a value for unary operations, or the events for streaming ones.</summary>
/// <remarks>Exactly one of <see cref="Value"/> and <see cref="Events"/> is set, except that a unary <see cref="Value"/> can be <see langword="null"/> (delete).</remarks>
public readonly struct A2AOperationResult
{
    private A2AOperationResult(object? value, IAsyncEnumerable<StreamResponse>? events)
    {
        Value = value;
        Events = events;
    }

    /// <summary>The response of a unary operation, or <see langword="null"/> (also for a delete).</summary>
    public object? Value { get; }

    /// <summary>The events of a streaming operation.</summary>
    public IAsyncEnumerable<StreamResponse>? Events { get; }

    /// <summary>Creates the result of a unary operation.</summary>
    /// <param name="value">The response. Null is allowed for <see cref="A2AOperation.DeleteTaskPushNotificationConfig"/>.</param>
    /// <returns>The result.</returns>
    public static A2AOperationResult FromValue(object? value) => new(value, null);

    /// <summary>Creates the result of a streaming operation.</summary>
    /// <param name="events">The events. Do not buffer them.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is <see langword="null"/>.</exception>
    public static A2AOperationResult FromEvents(IAsyncEnumerable<StreamResponse> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return new(null, events);
    }
}
