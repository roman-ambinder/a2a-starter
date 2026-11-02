using System.Net;
using System.Net.Sockets;
using A2A.Protocol;

namespace A2A.Server.Push;

/// <summary>
/// Checks webhook URLs (SSRF guard). Always requires http or https. Unless private networks are allowed it also requires
/// HTTPS and rejects hosts that resolve to loopback, link-local, private (RFC 1918), unique-local IPv6 and similar addresses.
/// Throws <see cref="A2AException"/> with <see cref="A2AErrorCode.InvalidParams"/>.
/// </summary>
public static class WebhookUrlValidator
{
    public static void Validate(Uri url, bool allowPrivateNetworks)
    {
        CheckScheme(url, allowPrivateNetworks);
        if (allowPrivateNetworks) return;
        Check(url, Resolve(url));
    }

    public static async ValueTask ValidateAsync(Uri url, bool allowPrivateNetworks, CancellationToken ct)
    {
        CheckScheme(url, allowPrivateNetworks);
        if (allowPrivateNetworks) return;
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(url.DnsSafeHost, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(url.DnsSafeHost, ct).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            throw Invalid($"The webhook host '{url.Host}' could not be resolved.");
        }
        Check(url, addresses);
    }

    private static void CheckScheme(Uri url, bool allowPrivateNetworks)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https"))
            throw Invalid("The webhook URL must be an absolute http or https URL.");
        if (!allowPrivateNetworks && url.Scheme != "https")
            throw Invalid("The webhook URL must use HTTPS.");
        if (!string.IsNullOrEmpty(url.UserInfo))
            throw Invalid("The webhook URL must not contain credentials.");
    }

    private static IPAddress[] Resolve(Uri url)
    {
        if (IPAddress.TryParse(url.DnsSafeHost, out var literal)) return [literal];
        try { return Dns.GetHostAddresses(url.DnsSafeHost); }
        catch (SocketException) { throw Invalid($"The webhook host '{url.Host}' could not be resolved."); }
    }

    private static void Check(Uri url, IPAddress[] addresses)
    {
        if (addresses.Length == 0)
            throw Invalid($"The webhook host '{url.Host}' could not be resolved.");
        if (addresses.Any(IsPrivate))
            throw Invalid("The webhook URL points to a private, loopback or link-local address.");
    }

    internal static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0                                  // 0.0.0.0/8
                   || b[0] == 10                              // RFC 1918
                   || (b[0] == 100 && b[1] is >= 64 and <= 127) // CGNAT 100.64/10
                   || (b[0] == 169 && b[1] == 254)            // link-local
                   || (b[0] == 172 && b[1] is >= 16 and <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || b[0] >= 224;                            // multicast, reserved, broadcast
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return address.Equals(IPAddress.IPv6Any)
                   || address.IsIPv6LinkLocal
                   || address.IsIPv6SiteLocal
                   || address.IsIPv6Multicast
                   || (b[0] & 0xFE) == 0xFC; // fc00::/7 unique local
        }
        return true;
    }

    private static A2AException Invalid(string message) => new(A2AErrorCode.InvalidParams, message);
}
