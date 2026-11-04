using Ambinder.A2A.Server.Push;

namespace Ambinder.A2A.Server.Hosting;

/// <summary>Settings for the A2A hosting layer. Configure through <see cref="Microsoft.Extensions.DependencyInjection.A2AServerCoreServiceCollectionExtensions.AddA2AServerCore"/>.</summary>
/// <remarks>Set the properties once, in the callback you pass to <c>AddA2AServer</c> or <c>AddA2AServerCore</c>. The values are checked at startup; a bad value fails with a message that says how to fix it.</remarks>
public sealed class A2AServerOptions
{
    /// <summary>
    /// Absolute public base URL of the HTTP listener, for example <c>http://localhost:5200</c>. Used to build the card
    /// interface URLs and as the push signature issuer. A trailing slash is ignored. Required: there is no default.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>Agent whose card is served at <c>/.well-known/agent-card.json</c>. Null (the default) means that URL returns 404.</summary>
    public string? DefaultAgent { get; set; }

    /// <summary>gRPC port per agent name. An agent without an entry gets no GRPC interface on its card. Default: empty.</summary>
    public IDictionary<string, int> GrpcPorts { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Tenant per agent name. When an agent has an entry, every interface on its card carries that <c>tenant</c>, and the server
    /// rejects a request that names a different tenant with <c>InvalidParams</c>. A request with no tenant is treated as this one.
    /// Default: empty (no interface declares a tenant).
    /// </summary>
    public IDictionary<string, string> InterfaceTenants { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// <c>max-age</c> of the <c>Cache-Control</c> header on public agent card responses (spec 8.6.1). Default 5 minutes (a <see cref="TimeSpan"/>).
    /// <see cref="TimeSpan.Zero"/> sends <c>no-cache</c> (clients revalidate with the ETag every time).
    /// </summary>
    public TimeSpan CardCacheMaxAge { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Largest accepted request body, in bytes. Larger bodies get HTTP 413. Default 4 MiB (4,194,304). Must be positive.</summary>
    public long MaxRequestBodyBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// Push delivery settings. This exact instance is used by the task manager and the push sender.
    /// <see cref="PushOptions.Issuer"/> is overwritten with <see cref="PublicBaseUrl"/>. Change its properties; you cannot replace it.
    /// </summary>
    public PushOptions Push { get; } = new();

    /// <summary>
    /// Names of response headers (gRPC: response metadata) that middleware may set through <see cref="A2AResponseMetadata"/>, besides the names that
    /// start with <c>A2A-</c>. Names are not case-sensitive. A name that is not allowed is dropped and logged once at debug level.
    /// Default: empty (only <c>A2A-*</c> names are sent).
    /// </summary>
    public ISet<string> ResponseHeaderAllowList { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary><see cref="PublicBaseUrl"/> without a trailing slash.</summary>
    internal string BaseUrl => PublicBaseUrl.TrimEnd('/');
}
