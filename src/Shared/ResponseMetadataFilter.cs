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

    // Each dropped name is logged once per process, so a middleware that sets it on every call does not flood the log.
    // The set holds at most MaxLoggedNames names (names can come from request data); when it is full, nothing more is logged.
    private const int MaxLoggedNames = 256;
    private static readonly ConcurrentDictionary<string, byte> Logged = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The entries to send. An entry is dropped (debug log, never an exception) when its name is not allowed, is not a valid header name
    /// (an RFC 9110 token), or its value has a control character other than tab. For gRPC (<paramref name="grpc"/>) the value must also be
    /// printable ASCII and the name is checked in its lower-case form, which is what the transport sends.
    /// </summary>
    public static List<KeyValuePair<string, string>> Allowed(A2AResponseMetadata metadata, ISet<string> allowList, ILogger logger, bool grpc = false)
        => Allowed(metadata.Entries, allowList, logger, grpc);

    public static List<KeyValuePair<string, string>> Allowed(IEnumerable<KeyValuePair<string, string>> entries, ISet<string> allowList, ILogger logger, bool grpc = false)
    {
        var allowed = new List<KeyValuePair<string, string>>();
        foreach (var entry in entries)
        {
            if (!(entry.Key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || allowList.Contains(entry.Key)))
            {
                if (ShouldLog(entry.Key)) DroppedResponseMetadata(logger, entry.Key);
            }
            else if (!IsValidName(entry.Key) || !IsValidValue(entry.Value, grpc))
            {
                if (ShouldLog("invalid:" + entry.Key)) LogInvalid(logger, entry.Key);
            }
            else
                allowed.Add(entry);
        }
        return allowed;
    }

    private static bool ShouldLog(string key) => Logged.Count < MaxLoggedNames && Logged.TryAdd(key, 0);

    private static bool IsValidName(string name)
    {
        if (name.Length == 0) return false;
        foreach (var c in name)
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' || "!#$%&'*+-.^_`|~".Contains(c))) return false;
        return true;
    }

    private static bool IsValidValue(string value, bool grpc)
    {
        foreach (var c in value)
        {
            if (grpc ? c is < ' ' or > '~' : c is (< ' ' and not '\t') or '\u007f') return false;
        }
        return true;
    }

    private static void LogInvalid(ILogger logger, string name)
    {
        if (!logger.IsEnabled(LogLevel.Debug)) return;
        var printable = Printable(name);
        InvalidResponseMetadata(logger, printable);
    }

    // The name is logged, so it must not carry control characters itself.
    private static string Printable(string name)
        => string.Create(Math.Min(name.Length, 64), name, (span, n) => { for (var i = 0; i < span.Length; i++) span[i] = n[i] is >= ' ' and <= '~' ? n[i] : '?'; });

    [LoggerMessage(EventId = LogEventIds.DroppedResponseMetadata, EventName = nameof(DroppedResponseMetadata), Level = LogLevel.Debug,
        Message = "Response metadata '{Name}' was not sent. Names must start with A2A- or be added to A2AServerOptions.ResponseHeaderAllowList.")]
    private static partial void DroppedResponseMetadata(ILogger logger, string name);

    [LoggerMessage(EventId = LogEventIds.InvalidResponseMetadata, EventName = nameof(InvalidResponseMetadata), Level = LogLevel.Debug,
        Message = "Response metadata '{Name}' was not sent. The name must be a valid header name and the value must not contain control characters (gRPC: only printable ASCII).")]
    private static partial void InvalidResponseMetadata(ILogger logger, string name);
}
