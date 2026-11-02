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
    /// Throws <see cref="NotSupportedException"/>, listing the card's bindings, when there is none.
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
                : $"None of the agent card's bindings is supported ({string.Join(", ", interfaces.Select(i => i.ProtocolBinding))}); " +
                  $"this client supports {string.Join(", ", SupportedBindings)}.");
        }

        if (!Uri.TryCreate(chosen.Url, UriKind.Absolute, out var url))
            throw new NotSupportedException($"The agent card's {chosen.ProtocolBinding} interface has an invalid URL: '{chosen.Url}'.");
        if (Is(chosen, A2AConstants.BindingRest)) return new RestA2AClient(url, card, options);
        if (Is(chosen, A2AConstants.BindingGrpc)) return new GrpcA2AClient(url, card, options);
        return new JsonRpcA2AClient(url, card, options);
    }

    private static bool Is(AgentInterface i, string binding) => string.Equals(i.ProtocolBinding, binding, StringComparison.OrdinalIgnoreCase);

    private static bool IsSupported(AgentInterface i) => SupportedBindings.Any(b => Is(i, b));
}
