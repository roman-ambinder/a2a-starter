using System.Collections.Concurrent;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Tasks;

/// <summary>In-memory <see cref="ITaskStore"/>. Copies tasks on save and on read.</summary>
internal sealed class InMemoryTaskStore : ITaskStore
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    private readonly ConcurrentDictionary<(string Agent, string TaskId), StoredTask> _tasks = new();

    public ValueTask<StoredTask?> GetAsync(string agentName, string taskId, CancellationToken ct)
        => ValueTask.FromResult(_tasks.TryGetValue((agentName, taskId), out var t) ? Clone(t) : null);

    public ValueTask SaveAsync(StoredTask task, CancellationToken ct)
    {
        _tasks[(task.AgentName, task.Task.Id)] = Clone(task);
        return ValueTask.CompletedTask;
    }

    public ValueTask<ListTasksResponse> ListAsync(string agentName, string owner, ListTasksRequest request, CancellationToken ct)
    {
        var pageSize = request.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
            throw new A2AException(A2AErrorCode.InvalidParams, $"pageSize must be between 1 and {MaxPageSize}. Send a value in that range, or omit it.");
        if (request.HistoryLength is < 0)
            throw new A2AException(A2AErrorCode.InvalidParams, "historyLength must not be negative. Send 0 or more, or omit it.");

        (DateTimeOffset Ts, string Id)? cursor = null;
        if (!string.IsNullOrEmpty(request.PageToken))
        {
            if (!PageToken.TryDecode(request.PageToken, out var ts, out var id))
                throw new A2AException(A2AErrorCode.InvalidParams, "Invalid pageToken. Send the nextPageToken from the previous page, or omit it.");
            cursor = (ts, id);
        }

        var matching = _tasks.Values
            .Where(t => t.AgentName == agentName && t.Owner == owner && Matches(t.Task, request))
            .OrderByDescending(t => SortTime(t.Task))
            .ThenByDescending(t => t.Task.Id, StringComparer.Ordinal)
            .ToList();

        // Sorted descending: the next page holds items strictly "less than" the cursor.
        var rest = cursor is { } c
            ? matching.Where(t => Compare(SortTime(t.Task), t.Task.Id, c.Ts, c.Id) < 0).ToList()
            : matching;

        var page = rest.Take(pageSize).ToList();
        var next = rest.Count > page.Count
            ? PageToken.Encode(SortTime(page[^1].Task), page[^1].Task.Id)
            : "";

        var includeArtifacts = request.IncludeArtifacts ?? false;
        return ValueTask.FromResult(new ListTasksResponse
        {
            Tasks = page.Select(t => Shape(t.Task, request.HistoryLength, includeArtifacts)).ToList(),
            NextPageToken = next,
            PageSize = pageSize,
            TotalSize = matching.Count,
        });
    }

    // Tasks without a timestamp sort last (smallest value in descending order).
    private static DateTimeOffset SortTime(AgentTask t) => t.Status.Timestamp ?? DateTimeOffset.MinValue;

    private static int Compare(DateTimeOffset ts, string id, DateTimeOffset otherTs, string otherId)
    {
        var byTime = ts.UtcTicks.CompareTo(otherTs.UtcTicks);
        return byTime != 0 ? byTime : string.CompareOrdinal(id, otherId);
    }

    private static bool Matches(AgentTask t, ListTasksRequest r)
    {
        if (r.ContextId is { Length: > 0 } ctx && t.ContextId != ctx) return false;
        if (r.Status is { } s && s != TaskState.Unspecified && t.Status.State != s) return false;
        if (r.StatusTimestampAfter is { } after && !(t.Status.Timestamp is { } ts && ts > after)) return false;
        return true;
    }

    private static AgentTask Shape(AgentTask t, int? historyLength, bool includeArtifacts)
    {
        var history = historyLength switch
        {
            null => t.History,
            0 => [],
            var n => t.History.Skip(Math.Max(0, t.History.Count - n.Value)),
        };
        return t with
        {
            History = history.ToList(),
            Artifacts = includeArtifacts ? t.Artifacts.ToList() : [],
        };
    }

    private static StoredTask Clone(StoredTask s)
        => s with { Task = s.Task with { History = s.Task.History.ToList(), Artifacts = s.Task.Artifacts.ToList() } };
}
