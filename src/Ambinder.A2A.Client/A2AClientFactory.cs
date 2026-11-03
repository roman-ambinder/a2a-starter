using Ambinder.A2A.Client.Transports;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client;

/// <summary>Creates the right <see cref="IA2AClient"/> for an agent card.</summary>
public static class A2AClientFactory
{
    private static readonly string[] BuiltInBindings = [A2AConstants.BindingJsonRpc, A2AConstants.BindingRest];

    /// <summary>
    /// Picks the card interface whose binding equals <see cref="A2AClientOptions.PreferredBinding"/> (case-insensitive). Without a
    /// preference, or when the card does not offer it, the first interface in card order with a supported binding is used.
    /// Supported bindings are the built-in JSON-RPC and HTTP+JSON plus the bindings of <see cref="A2AClientOptions.Transports"/>
    /// (for example gRPC from the <c>Ambinder.A2A.Client.Grpc</c> package). A transport in <see cref="A2AClientOptions.Transports"/>
    /// takes the place of the built-in one for the same binding.
    /// Only interfaces whose <c>protocolVersion</c> (<c>Major.Minor</c>, patch ignored) is <see cref="A2AConstants.ProtocolVersion"/>
    /// are considered (spec 3.6, 8.3.2); an empty <c>protocolVersion</c> is taken as the current version.
    /// Throws <see cref="NotSupportedException"/>, listing the card's interfaces, when there is none. When the card lists a gRPC
    /// interface and no gRPC transport is registered, the message says to add the <c>Ambinder.A2A.Client.Grpc</c> package and
    /// <c>GrpcClientTransport.Instance</c> to <see cref="A2AClientOptions.Transports"/>. The chosen interface's
    /// <c>tenant</c> is sent on every request (spec 8.3.2); a request with a different tenant throws <see cref="ArgumentException"/>.
    /// The result is a <see cref="TracingA2AClient"/> (one span per call); its <see cref="TracingA2AClient.Inner"/> is the transport client.
    /// </summary>
    public static IA2AClient Create(AgentCard card, A2AClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(options);

        var transports = options.Transports;
        var supported = BuiltInBindings.Concat(transports.Select(t => t.ProtocolBinding)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        bool IsSupported(AgentInterface i) => supported.Any(b => Is(i, b)) && IsSupportedVersion(i.ProtocolVersion);

        var interfaces = card.SupportedInterfaces;
        var chosen = string.IsNullOrWhiteSpace(options.PreferredBinding)
            ? null
            : interfaces.FirstOrDefault(i => IsSupported(i) && Is(i, options.PreferredBinding));
        chosen ??= interfaces.FirstOrDefault(IsSupported);

        if (chosen is null)
        {
            if (interfaces.Count == 0) throw new NotSupportedException("The agent card lists no supported interfaces.");
            var message = $"None of the agent card's interfaces is supported ({string.Join(", ", interfaces.Select(i => $"{i.ProtocolBinding}/{i.ProtocolVersion}"))}); " +
                          $"this client supports {string.Join(", ", supported)} at A2A version {A2AConstants.ProtocolVersion}.";
            if (interfaces.Any(i => Is(i, A2AConstants.BindingGrpc)) && !supported.Any(b => string.Equals(b, A2AConstants.BindingGrpc, StringComparison.OrdinalIgnoreCase)))
                message += " The card lists a GRPC interface: add the Ambinder.A2A.Client.Grpc package and GrpcClientTransport.Instance to A2AClientOptions.Transports.";
            throw new NotSupportedException(message);
        }

        // The interface's tenant is the default for requests that do not set one.
        var tenant = string.IsNullOrEmpty(chosen.Tenant) ? null : chosen.Tenant;

        var transport = transports.FirstOrDefault(t => Is(chosen, t.ProtocolBinding));
        if (transport is not null)
        {
            var created = transport.Create(chosen, card, options, tenant)
                          ?? throw new InvalidOperationException($"The {transport.ProtocolBinding} transport returned no client.");
            return new TracingA2AClient(created, card.Name, transport.TransportName, tenant);
        }

        if (!Uri.TryCreate(chosen.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new NotSupportedException($"The agent card's {chosen.ProtocolBinding} interface has an invalid URL: '{chosen.Url}'.");
        if (Is(chosen, A2AConstants.BindingRest))
            return new TracingA2AClient(new RestA2AClient(url, card, options, tenant), card.Name, A2ATelemetryNames.TransportRest, tenant);
        return new TracingA2AClient(new JsonRpcA2AClient(url, card, options, tenant), card.Name, A2ATelemetryNames.TransportJsonRpc, tenant);
    }

    private static bool Is(AgentInterface i, string binding) => string.Equals(i.ProtocolBinding, binding, StringComparison.OrdinalIgnoreCase);

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
