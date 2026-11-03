namespace A2A.Integration.Tests.Fixtures;

/// <summary>
/// Sets (or, for an empty value, removes) the <c>A2A-Version</c> header on every request, so a test can send any version through the
/// real client. A gRPC client sends its <c>a2a-version</c> metadata as the same HTTP header, so this works on all three bindings.
/// </summary>
public sealed class VersionRewriteHandler : DelegatingHandler
{
    private readonly string _version;

    public VersionRewriteHandler(string version) : base(new SocketsHttpHandler()) => _version = version;

    /// <summary>An HttpClient that sends <paramref name="version"/> instead of the one the client chose.</summary>
    public static HttpClient CreateClient(string version) => new(new VersionRewriteHandler(version)) { Timeout = TimeSpan.FromSeconds(30) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("A2A-Version");
        if (_version.Length > 0) request.Headers.TryAddWithoutValidation("A2A-Version", _version);
        return base.SendAsync(request, cancellationToken);
    }
}
