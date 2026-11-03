using Ambinder.A2A.Server.Push;

namespace Ambinder.A2A.Server.Hosting;

/// <summary>Settings for the A2A hosting layer. Configure through <see cref="A2AServiceCollectionExtensions.AddA2AServer"/>.</summary>
public sealed class A2AServerOptions
{
    /// <summary>
    /// Absolute public base URL of the HTTP listener, for example <c>http://localhost:5200</c>. Used to build the card
    /// interface URLs and as the push signature issuer. A trailing slash is ignored.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>Agent whose card is served at <c>/.well-known/agent-card.json</c>. Null → that URL returns 404.</summary>
    public string? DefaultAgent { get; set; }

    /// <summary>gRPC port per agent name. An agent without an entry gets no GRPC interface on its card.</summary>
    public IDictionary<string, int> GrpcPorts { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// <c>max-age</c> of the <c>Cache-Control</c> header on public agent card responses (spec 8.6.1). Default 5 minutes.
    /// <see cref="TimeSpan.Zero"/> sends <c>no-cache</c> (clients revalidate with the ETag every time).
    /// </summary>
    public TimeSpan CardCacheMaxAge { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Largest accepted request body. Larger bodies get HTTP 413.</summary>
    public long MaxRequestBodyBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// Push delivery settings. This exact instance is used by the task manager and the push sender.
    /// <see cref="PushOptions.Issuer"/> is overwritten with <see cref="PublicBaseUrl"/>.
    /// </summary>
    public PushOptions Push { get; } = new();

    /// <summary><see cref="PublicBaseUrl"/> without a trailing slash.</summary>
    internal string BaseUrl => PublicBaseUrl.TrimEnd('/');
}
