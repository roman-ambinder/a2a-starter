using System.Collections.Concurrent;
using Ambinder.A2A.Server;
using Microsoft.Extensions.Logging;

namespace Ambinder.A2A.AspNetCore.Transports;

/// <summary>
/// Picks the response metadata entries a transport may send: names that start with <c>A2A-</c> and the names in
/// <c>A2AServerOptions.ResponseHeaderAllowList</c>. Compiled into <c>Ambinder.A2A.AspNetCore</c> and <c>Ambinder.A2A.AspNetCore.Grpc</c>.
/// </summary>
internal static partial class ResponseMetadataFilter
{
    private const string Prefix = "A2A-";

    // A name is logged once per process, so a middleware that sets it on every call does not flood the log.
    private static readonly ConcurrentDictionary<string, byte> Logged = new(StringComparer.OrdinalIgnoreCase);

    public static List<KeyValuePair<string, string>> Allowed(A2AResponseMetadata metadata, ISet<string> allowList, ILogger logger)
    {
        var allowed = new List<KeyValuePair<string, string>>();
        foreach (var entry in metadata.Entries)
        {
            if (entry.Key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || allowList.Contains(entry.Key))
                allowed.Add(entry);
            else if (Logged.TryAdd(entry.Key, 0))
                DroppedResponseMetadata(logger, entry.Key);
        }
        return allowed;
    }

    [LoggerMessage(EventId = LogEventIds.DroppedResponseMetadata, EventName = nameof(DroppedResponseMetadata), Level = LogLevel.Debug,
        Message = "Response metadata '{Name}' was not sent. Names must start with A2A- or be added to A2AServerOptions.ResponseHeaderAllowList.")]
    private static partial void DroppedResponseMetadata(ILogger logger, string name);
}
