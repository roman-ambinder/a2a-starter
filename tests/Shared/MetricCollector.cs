using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace A2A.Tests.Shared;

/// <summary>
/// Records the long measurements of one meter. Metric tags carry no trace ID, so every test that uses this goes in the
/// "Telemetry" collection (no parallel runs).
/// </summary>
public sealed class MetricCollector : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<(string Instrument, long Value, IReadOnlyDictionary<string, object?> Tags)> _measurements = new();

    public MetricCollector(string meter)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meter) listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var tag in tags) dict[tag.Key] = tag.Value;
            _measurements.Enqueue((instrument.Name, value, dict));
        });
        _listener.Start();
    }

    public IReadOnlyList<(string Instrument, long Value, IReadOnlyDictionary<string, object?> Tags)> Measurements => [.. _measurements];

    public void Dispose() => _listener.Dispose();
}
