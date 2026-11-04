namespace Ambinder.A2A.Server;

/// <summary>
/// Response metadata set by middleware: HTTP response headers, or gRPC response headers. Transports write it before the body.
/// </summary>
/// <remarks>Names are case-insensitive. Set values before the first byte of the response. One instance serves one call and is thread-safe.</remarks>
public sealed class A2AResponseMetadata
{
    // Nothing is allocated until first use: most calls never set a header, so the default case has no lock and no dictionary.
    private object? _lock;
    private Dictionary<string, string>? _entries;
    private bool _committed;

    private object _gate => _lock ?? Interlocked.CompareExchange(ref _lock, new object(), null) ?? _lock!;

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
            (_entries ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))[name] = value;
        }
    }

    /// <summary>Reads a value.</summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value, when set.</param>
    /// <returns><see langword="true"/> when the name was set.</returns>
    public bool TryGet(string name, out string? value)
    {
        if (Volatile.Read(ref _entries) is null)
        {
            value = null;
            return false;
        }
        lock (_gate) return _entries!.TryGetValue(name, out value);
    }

    /// <summary>A snapshot of the values set so far.</summary>
    public IEnumerable<KeyValuePair<string, string>> Entries
    {
        get
        {
            if (Volatile.Read(ref _entries) is null) return [];
            lock (_gate) return _entries!.ToArray();
        }
    }

    /// <summary>Whether the response has started, so no more values can be set.</summary>
    public bool IsCommitted
    {
        get => Volatile.Read(ref _committed);
    }

    /// <summary>Called by transports when they write the first byte. Later calls to <see cref="Set"/> throw.</summary>
    public void MarkCommitted()
    {
        lock (_gate) Volatile.Write(ref _committed, true);
    }
}
