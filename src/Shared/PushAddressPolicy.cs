using System.Net;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Push;

/// <summary>
/// The address rules of the SSRF guard, built from <see cref="PushOptions"/>: which resolved addresses may be used
/// (<see cref="PushOptions.AllowPrivateNetworks"/>, <see cref="PushOptions.AllowedNetworks"/>) and which webhook hosts
/// may be named (<see cref="PushOptions.AllowedHosts"/>). Read-only after construction, so it is safe to share.
/// </summary>
// Linked as one source file into Ambinder.A2A.Server and Ambinder.A2A.AspNetCore (spec 01 rule 10). Warning: a project that sees both copies through InternalsVisibleTo gets CS0433 if it names this type; test it from Ambinder.A2A.Server.Tests only.
internal sealed class PushAddressPolicy
{
    private readonly IPNetwork[] _networks;
    private readonly string[] _hosts;

    public PushAddressPolicy(PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        AllowPrivateNetworks = options.AllowPrivateNetworks;
        MaxUrlLength = options.MaxUrlLength;
        // An entry that does not parse is skipped: that only narrows what is allowed. PushOptionsValidator reports it at startup.
        _networks = [.. options.AllowedNetworks.Select(n => IPNetwork.TryParse(n, out var network) ? (IPNetwork?)network : null).OfType<IPNetwork>()];
        _hosts = [.. options.AllowedHosts.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim().ToLowerInvariant())];
    }

    /// <summary>True when private, loopback and link-local addresses are allowed everywhere.</summary>
    public bool AllowPrivateNetworks { get; }

    /// <summary>The longest webhook URL accepted at config create.</summary>
    public int MaxUrlLength { get; }

    /// <summary>True when the address is public, private networks are allowed, or it is inside an allowed CIDR range.</summary>
    public bool IsAllowed(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (AllowPrivateNetworks || !WebhookUrlValidator.IsPrivate(address)) return true;
        foreach (var network in _networks)
        {
            if (network.Contains(address)) return true;
        }
        return false;
    }

    /// <summary>Checks the webhook host against <see cref="PushOptions.AllowedHosts"/>. Does nothing when the list is empty.</summary>
    /// <exception cref="A2AException">The host matches no entry (<see cref="A2AErrorCode.InvalidParams"/>).</exception>
    public void CheckHost(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (_hosts.Length == 0) return;
        var host = url.DnsSafeHost.ToLowerInvariant();
        foreach (var entry in _hosts)
        {
            if (entry.StartsWith("*.", StringComparison.Ordinal)
                ? host.Length > entry.Length - 1 && host.EndsWith(entry[1..], StringComparison.Ordinal)
                : host == entry)
                return;
        }
        throw new A2AException(A2AErrorCode.InvalidParams,
            $"The webhook host '{url.Host}' is not in A2A:Push:AllowedHosts. Use a webhook host from that list, or ask the operator to add it.");
    }
}
