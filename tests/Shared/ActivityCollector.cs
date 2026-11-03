using System.Collections.Concurrent;
using System.Diagnostics;

namespace A2A.Tests.Shared;

/// <summary>
/// Records the activities of the given sources, but only those under <see cref="Root"/>. The sampler drops everything else, so parallel
/// tests never see each other's activities and no other test gets extra activities or <c>traceparent</c> headers.
/// This holds only while no OpenTelemetry SDK runs: tests that start one are in a collection with parallelization off.
/// </summary>
public sealed class ActivityCollector : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<Activity> _stopped = new();

    /// <summary>Pass as the only source to listen to every source (still limited to Root's trace).</summary>
    public const string AllSources = "*";

    public ActivityCollector(params string[] sources)
    {
        // A fresh W3C trace ID per collector.
        // The random parent ID also keeps this root from joining another collector's trace (Activity.Current).
        Root = new Activity("test.root").SetIdFormat(ActivityIdFormat.W3C)
            .SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom());
        Root.Start();
        var traceId = Root.TraceId;
        var names = new HashSet<string>(sources);
        _listener = new ActivityListener
        {
            ShouldListenTo = source => names.Contains(AllSources) || names.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Parent.TraceId == traceId ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None,
            ActivityStopped = _stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>The test's root activity. Start calls under it (it is <see cref="Activity.Current"/> in the test's context).</summary>
    public Activity Root { get; }

    public IReadOnlyList<Activity> Stopped => [.. _stopped];

    /// <summary>The one stopped activity with this name. Fails when there are none or several.</summary>
    public Activity Single(string name) => Assert.Single(Stopped, a => a.OperationName == name);

    public async Task<Activity> WaitForAsync(string name, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var found = Stopped.FirstOrDefault(a => a.OperationName == name);
            if (found is not null) return found;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException($"No activity '{name}' stopped within {timeout}.");
    }

    /// <summary>Waits until at least <paramref name="count"/> activities with this name have stopped; returns them.</summary>
    public async Task<IReadOnlyList<Activity>> WaitForCountAsync(string name, int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var found = Stopped.Where(a => a.OperationName == name).ToList();
            if (found.Count >= count) return found;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Only {found.Count} of {count} activities '{name}' stopped within {timeout}.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
        Root.Stop();
    }
}
