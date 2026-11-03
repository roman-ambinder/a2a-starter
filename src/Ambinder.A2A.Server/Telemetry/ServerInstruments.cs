using System.Diagnostics;
using System.Diagnostics.Metrics;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Telemetry;

/// <summary>The server's activity source and metrics. Created once; they cost nothing when no listener is attached.</summary>
internal static class ServerInstruments
{
    public static readonly ActivitySource Source = new(A2ATelemetryNames.ServerSource);

    public static readonly Meter Meter = new(A2ATelemetryNames.ServerMeter);

    /// <summary>Tasks that reached a final state. Tags: agent, state.</summary>
    public static readonly Counter<long> TasksCompleted = Meter.CreateCounter<long>(A2ATelemetryNames.TasksCompleted,
        unit: "{task}", description: "Tasks that reached a final state.");

    /// <summary>Streaming calls in progress. Tag: agent.</summary>
    public static readonly UpDownCounter<long> StreamsActive = Meter.CreateUpDownCounter<long>(A2ATelemetryNames.StreamsActive,
        unit: "{stream}", description: "Streaming calls in progress.");

    /// <summary>Push notification deliveries. Tags: agent, outcome.</summary>
    public static readonly Counter<long> PushDeliveries = Meter.CreateCounter<long>(A2ATelemetryNames.PushDeliveries,
        unit: "{delivery}", description: "Push notification deliveries by outcome.");
}
