using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Ambinder.A2A.Client;

/// <summary>
/// A small, case-insensitive set of request headers (HTTP headers or gRPC metadata) that works the same on every transport.
/// </summary>
/// <remarks>
/// The collection hides the <c>Authorization</c> value from readers and enumeration, so an interceptor cannot read a token.
/// A credential provider sets it with <see cref="SetAuthorization"/>. Writing <c>Authorization</c> through the indexer or
/// <c>Add</c> also stores it in that hidden slot. An instance belongs to one call and is not thread-safe.
/// </remarks>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "The name A2ARequestHeaders is fixed by spec 10.")]
public sealed class A2ARequestHeaders : IDictionary<string, string>
{
    private const string AuthorizationName = "Authorization";

    private readonly Dictionary<string, string> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, string?>? _reservedCheck;

    /// <summary>Initializes an empty collection.</summary>
    public A2ARequestHeaders()
    {
    }

    internal A2ARequestHeaders(Func<string, string?> reservedCheck) => _reservedCheck = reservedCheck;

    /// <summary>Sets the <c>Authorization</c> header value. The value is not visible through the collection.</summary>
    /// <param name="value">The full header value, for example <c>Bearer abc</c>.</param>
    public void SetAuthorization(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        ValidateValue(AuthorizationName, value);
        AuthorizationForTransport = value;
    }

    internal string? AuthorizationForTransport { get; private set; }

    internal bool HasAuthorization => AuthorizationForTransport is not null;

    internal void ClearAuthorization() => AuthorizationForTransport = null;

    private static bool IsAuthorization(string key) => string.Equals(key, AuthorizationName, StringComparison.OrdinalIgnoreCase);

    private static void ValidateValue(string name, string value)
    {
        foreach (var c in value)
        {
            if (c is '\r' or '\n' or '\0')
            {
                throw new ArgumentException($"The value of header '{name}' contains a line break or null character.", nameof(value));
            }
        }
    }

    private void Store(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        foreach (var c in key)
        {
            if (c is '\r' or '\n' or '\0' or ':' or ' ')
            {
                throw new ArgumentException($"'{key}' is not a valid header name.", nameof(key));
            }
        }

        var reserved = _reservedCheck?.Invoke(key);
        if (reserved is not null)
        {
            throw new ArgumentException(reserved, nameof(key));
        }

        ValidateValue(key, value);
        if (IsAuthorization(key))
        {
            AuthorizationForTransport = value;
        }
        else
        {
            _items[key] = value;
        }
    }

    /// <inheritdoc />
    public string this[string key]
    {
        get => _items[key];
        set => Store(key, value);
    }

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public ICollection<string> Keys => _items.Keys;

    /// <inheritdoc />
    public ICollection<string> Values => _items.Values;

    /// <inheritdoc />
    public void Add(string key, string value)
    {
        if (!IsAuthorization(key) && _items.ContainsKey(key))
        {
            throw new ArgumentException($"A header named '{key}' already exists.", nameof(key));
        }

        Store(key, value);
    }

    /// <inheritdoc />
    public void Add(KeyValuePair<string, string> item) => Add(item.Key, item.Value);

    /// <inheritdoc />
    public void Clear() => _items.Clear();

    /// <inheritdoc />
    public bool Contains(KeyValuePair<string, string> item)
        => _items.TryGetValue(item.Key, out var v) && string.Equals(v, item.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public bool ContainsKey(string key) => _items.ContainsKey(key);

    /// <inheritdoc />
    public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
        => ((ICollection<KeyValuePair<string, string>>)_items).CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public bool Remove(string key) => _items.Remove(key);

    /// <inheritdoc />
    public bool Remove(KeyValuePair<string, string> item)
        => Contains(item) && _items.Remove(item.Key);

    /// <inheritdoc />
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) => _items.TryGetValue(key, out value);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
