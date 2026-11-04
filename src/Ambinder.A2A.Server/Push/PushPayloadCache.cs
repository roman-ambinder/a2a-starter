using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Tasks;

namespace Ambinder.A2A.Server.Push;

/// <summary>Formats an event once per formatter during one task write and shares the bytes between configs.</summary>
/// <remarks>Not thread-safe. Create one per write. The key is the formatter instance, so a formatter's output must not depend on the config.</remarks>
internal sealed class PushPayloadCache
{
    private readonly Dictionary<IPushPayloadFormatter, PushPayload> _payloads = new(ReferenceEqualityComparer.Instance);

    internal PushPayload GetOrFormat(IPushPayloadFormatter formatter, StoredTask task, StreamResponse evt, TaskPushNotificationConfig config)
    {
        if (!_payloads.TryGetValue(formatter, out var payload))
        {
            payload = formatter.Format(task, evt, config);
            _payloads[formatter] = payload;
        }
        return payload;
    }
}
