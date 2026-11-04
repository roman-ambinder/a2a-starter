namespace Ambinder.A2A.Server.Tests.Telemetry;

/// <summary>Tests that read metrics. Metric tags carry no trace ID, so these tests must not run in parallel.</summary>
[CollectionDefinition("Telemetry", DisableParallelization = true)]
public sealed class TelemetryCollection;
