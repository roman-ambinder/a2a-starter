using A2A.Client.Transports;
using A2A.Protocol;

namespace A2A.Client;

/// <summary>Creates the right <see cref="IA2AClient"/> for an agent card.</summary>
public static class A2AClientFactory
{
    private static readonly string[] SupportedBindings = [A2AConstants.BindingJsonRpc, A2AConstants.BindingRest, A2AConstants.BindingGrpc];

    /// <summary>
    /// Picks the card interface whose binding equals <see cref="A2AClientOptions.PreferredBinding"/> (case-insensitive). Without a
    /// preference, or when the card does not offer it, the first interface in card order with a supported binding is used.
    /// Only interfaces whose <c>protocolVersion</c> (<c>Major.Minor</c>, patch ignored) is <see cref="A2AConstants.ProtocolVersion"/>
    /// are considered (spec 3.6, 8.3.2); an empty <c>protocolVersion</c> is taken as the current version.
    /// Throws <see cref="NotSupportedException"/>, listing the card's interfaces, when there is none. The chosen interface's
    /// <c>tenant</c> is sent on every request (spec 8.3.2); a request with a different tenant throws <see cref="ArgumentException"/>.
    /// The result is a <see cref="TracingA2AClient"/> (one span per call); its <see cref="TracingA2AClient.Inner"/> is the transport client.
    /// </summary>
    public static IA2AClient Create(AgentCard card, A2AClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(options);

        var interfaces = card.SupportedInterfaces;
        var chosen = string.IsNullOrWhiteSpace(options.PreferredBinding)
            ? null
            : interfaces.FirstOrDefault(i => IsSupported(i) && Is(i, options.PreferredBinding));
        chosen ??= interfaces.FirstOrDefault(IsSupported);

        if (chosen is null)
        {
            throw new NotSupportedException(interfaces.Count == 0
                ? "The agent card lists no supported interfaces."
                : $"None of the agent card's interfaces is supported ({string.Join(", ", interfaces.Select(i => $"{i.ProtocolBinding}/{i.ProtocolVersion}"))}); " +
                  $"this client supports {string.Join(", ", SupportedBindings)} at A2A version {A2AConstants.ProtocolVersion}." +
                  (interfaces.Any(i => IsMinor(i.ProtocolVersion, "0.3"))
                      ? " This card offers A2A 0.3 only for some interfaces; add the Ambinder.A2A.Compat.V0_3 package to call them."
                      : ""));
        }

        var address = Is(chosen, A2AConstants.BindingGrpc) ? GrpcAddress(chosen.Url, options.GrpcUseTls) : chosen.Url;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new NotSupportedException($"The agent card's {chosen.ProtocolBinding} interface has an invalid URL: '{chosen.Url}'.");
        // The interface's tenant is the default for requests that do not set one.
        var tenant = string.IsNullOrEmpty(chosen.Tenant) ? null : chosen.Tenant;
        // The version header is the selected interface's Major.Minor; an empty interface version means the current one.
        var version = MajorMinor(chosen.ProtocolVersion) ?? A2AConstants.ProtocolVersion;
        if (Is(chosen, A2AConstants.BindingRest))
            return new TracingA2AClient(new RestA2AClient(url, card, options, tenant, version), card.Name, A2ATelemetryNames.TransportRest, tenant);
        if (Is(chosen, A2AConstants.BindingGrpc))
            return new TracingA2AClient(new GrpcA2AClient(url, card, options, tenant, version), card.Name, A2ATelemetryNames.TransportGrpc, tenant);
        return new TracingA2AClient(new JsonRpcA2AClient(url, card, options, tenant, version), card.Name, A2ATelemetryNames.TransportJsonRpc, tenant);
    }

    /// <summary>
    /// A gRPC interface URL as a channel address. <c>http://</c> / <c>https://</c> URLs are used as is. A bare <c>host:port</c>
    /// gets <c>https://</c>, or <c>http://</c> when <paramref name="useTls"/> is false, or (null) when the host is loopback.
    /// </summary>
    internal static string GrpcAddress(string url, bool? useTls)
    {
        if (url.Contains("://", StringComparison.Ordinal)) return url;
        var tls = useTls ?? !IsLoopback(url);
        return (tls ? "https://" : "http://") + url;
    }

    private static bool IsLoopback(string hostPort)
    {
        if (!Uri.TryCreate("http://" + hostPort, UriKind.Absolute, out var uri)) return false;
        return uri.IsLoopback;
    }

    private static bool Is(AgentInterface i, string binding) => string.Equals(i.ProtocolBinding, binding, StringComparison.OrdinalIgnoreCase);

    private static bool IsSupported(AgentInterface i) => SupportedBindings.Any(b => Is(i, b)) && IsSupportedVersion(i.ProtocolVersion);

    /// <summary>True when <paramref name="version"/> has the given <c>Major.Minor</c> (a patch part is ignored).</summary>
    private static bool IsMinor(string? version, string majorMinor)
    {
        var parts = version?.Trim().Split('.');
        return parts is { Length: 2 or 3 } && $"{parts[0]}.{parts[1]}" == majorMinor;
    }

    /// <summary><c>Major.Minor</c> of a version (a patch part is dropped), or null for an empty or unparseable value.</summary>
    private static string? MajorMinor(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var parts = version.Trim().Split('.');
        return parts.Length is 2 or 3
               && int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var major)
               && int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var minor)
            ? $"{major}.{minor}"
            : null;
    }

    /// <summary>True for an empty version or one whose <c>Major.Minor</c> equals <see cref="A2AConstants.ProtocolVersion"/>.</summary>
    internal static bool IsSupportedVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return true;
        var parts = version.Trim().Split('.');
        return parts.Length is 2 or 3
               && int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var major)
               && int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var minor)
               && $"{major}.{minor}" == A2AConstants.ProtocolVersion;
    }
}
