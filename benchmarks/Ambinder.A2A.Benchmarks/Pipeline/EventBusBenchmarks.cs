using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Tasks;
using BenchmarkDotNet.Attributes;

namespace Ambinder.A2A.Benchmarks.Pipeline;

/// <summary>
/// Publish through the in-process event broker (TaskEventBroker via InternalsVisibleTo; ITaskEventBus once package 06 lands).
/// Subscribers drain on background tasks started in IterationSetup. IterationCleanup checks that every subscriber got every
/// event, in order, and throws otherwise.
/// </summary>
[BenchmarkCategory(Categories.Pipeline)]
public class EventBusBenchmarks
{
    private const string Agent = "bench";
    private const string TaskId = "task-1";
    private const int Events = 100; // 99 working updates, then one completed status that ends every stream

    [Params(0, 1, 10)]
    public int Subscribers { get; set; }

    private StreamResponse[] _events = null!;
    private TaskEventBroker _broker = null!;
    private TaskEventBroker.Subscription[] _subs = null!;
    private Task<int>[] _drains = null!;

    [GlobalSetup]
    public void Setup()
    {
        _events = new StreamResponse[Events];
        for (var i = 0; i < Events; i++)
        {
            var last = i == Events - 1;
            _events[i] = i % 2 == 0 || last
                ? new StreamResponse
                {
                    StatusUpdate = new TaskStatusUpdateEvent
                    {
                        TaskId = TaskId,
                        ContextId = "ctx",
                        Status = new AgentTaskStatus { State = last ? TaskState.Completed : TaskState.Working },
                    },
                }
                : new StreamResponse
                {
                    ArtifactUpdate = new TaskArtifactUpdateEvent
                    {
                        TaskId = TaskId,
                        ContextId = "ctx",
                        Artifact = new Artifact { ArtifactId = "a" + i, Parts = [Part.FromText("chunk")] },
                        LastChunk = true,
                    },
                };
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _broker = new TaskEventBroker();
        _subs = new TaskEventBroker.Subscription[Subscribers];
        _drains = new Task<int>[Subscribers];
        for (var s = 0; s < Subscribers; s++)
        {
            var sub = _broker.Open(Agent, TaskId, CancellationToken.None);
            _subs[s] = sub;
            _drains[s] = Task.Run(() => Drain(sub));
        }
    }

    private async Task<int> Drain(TaskEventBroker.Subscription sub)
    {
        var count = 0;
        await foreach (var evt in sub.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (!ReferenceEquals(evt, _events[count]))
            {
                throw new InvalidOperationException($"Event {count} arrived out of order.");
            }
            count++;
        }
        return count;
    }

    [Benchmark(OperationsPerInvoke = Events)]
    public void Publish()
    {
        var events = _events;
        for (var i = 0; i < events.Length; i++)
        {
            _broker.Publish(Agent, TaskId, events[i]);
        }
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        // Short wait for the drains. The final event completes every channel, so they end by themselves.
        if (!Task.WaitAll(_drains, TimeSpan.FromSeconds(30)))
        {
            throw new InvalidOperationException("Subscribers did not finish draining.");
        }
        // BDN may call Publish more than once per iteration; the first call ends the streams, later calls have no subscriber.
        foreach (var d in _drains)
        {
            if (d.Result != Events)
            {
                throw new InvalidOperationException($"A subscriber received {d.Result} events, expected {Events}.");
            }
        }
        foreach (var s in _subs) s.Dispose();
        var left = _broker.SubscriberCount(Agent, TaskId);
        if (left != 0) throw new InvalidOperationException($"{left} subscribers are left after the final event.");
    }
}
