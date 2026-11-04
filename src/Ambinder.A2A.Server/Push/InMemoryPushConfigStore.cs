using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Push;

/// <summary>Keeps push configs in memory. Page tokens are the creation sequence number of the last item returned.</summary>
internal sealed class InMemoryPushConfigStore : IPushConfigStore
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    private readonly object _gate = new();
    private readonly Dictionary<(string Agent, string TaskId), Dictionary<string, (long Seq, TaskPushNotificationConfig Config)>> _tasks = new();
    private long _sequence;

    public ValueTask<TaskPushNotificationConfig> SaveAsync(string agentName, string taskId, TaskPushNotificationConfig config, CancellationToken ct)
    {
        var id = string.IsNullOrEmpty(config.Id) ? Guid.NewGuid().ToString() : config.Id;
        var stored = config with { Id = id, TaskId = taskId };
        lock (_gate)
        {
            if (!_tasks.TryGetValue((agentName, taskId), out var configs))
                _tasks[(agentName, taskId)] = configs = new();
            var seq = configs.TryGetValue(id, out var existing) ? existing.Seq : ++_sequence;
            configs[id] = (seq, stored);
        }
        return ValueTask.FromResult(stored);
    }

    public ValueTask<TaskPushNotificationConfig?> GetAsync(string agentName, string taskId, string configId, CancellationToken ct)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_tasks.TryGetValue((agentName, taskId), out var configs) && configs.TryGetValue(configId, out var entry)
                ? entry.Config
                : null);
        }
    }

    public ValueTask<ListTaskPushNotificationConfigsResponse> ListAsync(string agentName, string taskId, int? pageSize, string? pageToken, CancellationToken ct)
    {
        var size = pageSize ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize)
            throw new A2AException(A2AErrorCode.InvalidParams, $"pageSize must be between 1 and {MaxPageSize}. Send a value in that range, or omit it.");
        var after = 0L;
        if (!string.IsNullOrEmpty(pageToken) && !TryDecode(pageToken, out after))
            throw new A2AException(A2AErrorCode.InvalidParams, "pageToken is not valid. Send the nextPageToken from the previous page, or omit it.");

        List<(long Seq, TaskPushNotificationConfig Config)> page;
        bool more;
        lock (_gate)
        {
            var all = _tasks.TryGetValue((agentName, taskId), out var configs) ? configs.Values.Where(e => e.Seq > after).OrderBy(e => e.Seq).Take(size + 1).ToList() : [];
            more = all.Count > size;
            page = all.Take(size).ToList();
        }
        return ValueTask.FromResult(new ListTaskPushNotificationConfigsResponse
        {
            Configs = page.Select(e => e.Config).ToList(),
            NextPageToken = more ? Encode(page[^1].Seq) : null,
        });
    }

    public ValueTask<IReadOnlyList<TaskPushNotificationConfig>> GetAllAsync(string agentName, string taskId, CancellationToken ct)
    {
        lock (_gate)
        {
            IReadOnlyList<TaskPushNotificationConfig> all = _tasks.TryGetValue((agentName, taskId), out var configs)
                ? configs.Values.OrderBy(e => e.Seq).Select(e => e.Config).ToList()
                : [];
            return ValueTask.FromResult(all);
        }
    }

    public ValueTask DeleteAsync(string agentName, string taskId, string configId, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_tasks.TryGetValue((agentName, taskId), out var configs) && configs.Remove(configId) && configs.Count == 0)
                _tasks.Remove((agentName, taskId));
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAllAsync(string agentName, string taskId, CancellationToken ct)
    {
        lock (_gate) _tasks.Remove((agentName, taskId));
        return ValueTask.CompletedTask;
    }

    private static string Encode(long seq) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(seq.ToString(CultureInfo.InvariantCulture)));

    private static bool TryDecode(string token, out long seq)
    {
        seq = 0;
        if (!Base64Url.IsValid(token)) return false;
        try { return long.TryParse(Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token)), NumberStyles.None, CultureInfo.InvariantCulture, out seq); }
        catch (FormatException) { return false; }
    }
}
