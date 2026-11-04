using Ambinder.A2A.Client.Auth;

namespace Ambinder.A2A.Client;

/// <summary>Per-call settings for an <see cref="IA2AClient"/> operation.</summary>
/// <remarks>
/// Create one instance per call or share one read-only instance. The mutable collections (<see cref="Headers"/> and
/// <see cref="Items"/>) are not thread-safe, so do not change them while a call uses the instance.
/// </remarks>
public sealed class A2ARequestOptions
{
    private readonly A2ARequestHeaders _headers;

    /// <summary>Initializes empty options.</summary>
    public A2ARequestOptions() => _headers = new A2ARequestHeaders(CheckReserved);

    /// <summary>Gets extra headers (or gRPC metadata) for this call.</summary>
    /// <remarks>
    /// <c>A2A-Version</c>, <c>A2A-Extensions</c> and <c>Content-Type</c> are reserved. <c>Authorization</c> is reserved
    /// when <see cref="Credentials"/> is set. Setting a reserved header throws <see cref="ArgumentException"/>.
    /// </remarks>
    public IDictionary<string, string> Headers => _headers;

    /// <summary>Gets the extension URIs to request for this call. This replaces the client default list. <see langword="null"/> keeps the default.</summary>
    public IReadOnlyList<string>? Extensions { get; init; }

    /// <summary>Gets the timeout for this call. This overrides the per-operation default. <see langword="null"/> keeps the default.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Gets the per-user credentials for this call. They replace the client's own provider.</summary>
    public IA2ACredentialProvider? Credentials { get; init; }

    /// <summary>Gets a user assertion for providers that use the on-behalf-of flow.</summary>
    public string? UserAssertion { get; init; }

    /// <summary>Gets the identity key for <see cref="Credentials"/>. It lets the client cache the extended agent card per identity.</summary>
    public string? CredentialCacheKey { get; init; }

    /// <summary>Gets whether the client may retry this send after a transport failure. <see langword="null"/> keeps the client default (off).</summary>
    public bool? AllowSendRetry { get; init; }

    /// <summary>Gets a callback that receives <see cref="A2AResponseInfo"/> when the response (or, for a stream, its headers) arrives.</summary>
    public Action<A2AResponseInfo>? OnResponse { get; init; }

    /// <summary>Gets a bag of values passed to interceptors.</summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>Checks the headers again once the client's own credential provider is known.</summary>
    internal void ValidateHeaders(bool clientHasCredentialProvider)
    {
        if (clientHasCredentialProvider && Credentials is null && _headers.HasAuthorization)
        {
            throw new ArgumentException(AuthorizationMessage);
        }
    }

    private const string AuthorizationMessage =
        "The 'Authorization' header cannot be set through Headers when a credential provider is set. Use A2ARequestOptions.Credentials or the client's credential provider.";

    private string? CheckReserved(string name)
    {
        if (name.Equals("A2A-Version", StringComparison.OrdinalIgnoreCase))
        {
            return $"The '{name}' header cannot be set through Headers. The client sends the selected interface's protocol version.";
        }

        if (name.Equals("A2A-Extensions", StringComparison.OrdinalIgnoreCase))
        {
            return $"The '{name}' header cannot be set through Headers. Use A2ARequestOptions.Extensions.";
        }

        if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
        {
            return $"The '{name}' header cannot be set through Headers. It is set by the transport.";
        }

        if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && Credentials is not null)
        {
            return AuthorizationMessage;
        }

        return null;
    }
}
