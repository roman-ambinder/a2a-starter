namespace Ambinder.A2A.Server;

/// <summary>
/// Response metadata set by middleware: HTTP response headers, or gRPC response headers. Transports write it before the body.
/// </summary>
/// <remarks>Names are case-insensitive. Set values before the first byte of the response. One instance serves one call and is thread-safe.</remarks>
public sealed class A2AResponseMetadata
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _committed;

    /// <summary>Sets a value. A second call with the same name replaces the value.</summary>
    /// <param name="name">The header or metadata name.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="InvalidOperationException">The response has started.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public void Set(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            if (_committed)
                throw new InvalidOperationException("Response metadata must be set before the first byte of the response. Set it before calling next or before the first event.");
            _entries[name] = value;
        }
    }

    /// <summary>Reads a value.</summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value, when set.</param>
    /// <returns><see langword="true"/> when the name was set.</returns>
    public bool TryGet(string name, out string? value)
    {
        lock (_gate) return _entries.TryGetValue(name, out value);
    }

    /// <summary>A snapshot of the values set so far.</summary>
    public IEnumerable<KeyValuePair<string, string>> Entries
    {
        get { lock (_gate) return _entries.ToArray(); }
    }

    /// <summary>Whether the response has started, so no more values can be set.</summary>
    public bool IsCommitted
    {
        get { lock (_gate) return _committed; }
    }

    /// <summary>Called by transports when they write the first byte. Later calls to <see cref="Set"/> throw.</summary>
    public void MarkCommitted()
    {
        lock (_gate) _committed = true;
    }
}
