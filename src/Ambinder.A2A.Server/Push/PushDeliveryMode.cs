using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Server.Push;

/// <summary>
/// How push requests leave the process: directly (the connect-time address check applies) or through a proxy (the
/// per-attempt resolve-and-check applies). Worked out once from <see cref="PushOptions"/>.
/// </summary>
internal sealed class PushDeliveryMode
{
    public PushDeliveryMode(IOptions<PushOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        UsesProxy = IsProxy(value);
        ProxyDescription = Describe(value);
    }

    /// <summary>True when deliveries go through <see cref="PushOptions.Proxy"/> or the system proxy.</summary>
    public bool UsesProxy { get; }

    /// <summary>The proxy for logs: <c>scheme://host:port</c> only, never credentials, path or query. Empty when no proxy is used.</summary>
    public string ProxyDescription { get; }

    internal static bool IsProxy(PushOptions options) => options.Proxy is not null || options.UseSystemProxy;

    internal static string Describe(PushOptions options)
    {
        if (options.Proxy is { IsAbsoluteUri: true } proxy)
            return $"{proxy.Scheme}://{proxy.Host}:{proxy.Port}"; // GetLeftPart would keep the user info
        if (options.Proxy is not null) return "(invalid proxy address)";
        return options.UseSystemProxy ? "the system proxy" : string.Empty;
    }
}
