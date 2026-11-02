using System.Net;
using System.Net.Http;
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
            return b[0] == 0                                    // 0.0.0.0/8
                   || b[0] == 10                                // RFC 1918
                   || (b[0] == 100 && b[1] is >= 64 and <= 127) // CGNAT 100.64/10
                   || (b[0] == 169 && b[1] == 254)              // link-local
                   || (b[0] == 172 && b[1] is >= 16 and <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2) // 192.0.0.0/24, 192.0.2.0/24
                   || (b[0] == 198 && b[1] is 18 or 19)         // 198.18.0.0/15
                   || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                   || (b[0] == 203 && b[1] == 0 && b[2] == 113)
                   || b[0] >= 224;                              // multicast, reserved, broadcast
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            if (address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC) // fc00::/7 unique local
                return true;
            // Forms that embed an IPv4 address: check the embedded one.
            if (b[..12].All(x => x == 0)) return IsPrivate(new IPAddress(b[12..]));                       // ::a.b.c.d
            if (b[0] == 0 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b[4..12].All(x => x == 0))   // 64:ff9b::/96 NAT64
                return IsPrivate(new IPAddress(b[12..]));
            if (b[0] == 0x20 && b[1] == 0x02) return IsPrivate(new IPAddress(b[2..6]));                   // 2002::/16 6to4
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0 && b[3] == 0)                                   // 2001::/32 Teredo
                return IsPrivate(new IPAddress(b[4..8]))
                       || IsPrivate(new IPAddress([(byte)~b[12], (byte)~b[13], (byte)~b[14], (byte)~b[15]]));
            return false;
        }
        return true;
    }

    /// <summary>Resolves the host and rejects private addresses (unless allowed). Returns the checked addresses to connect to.</summary>
    internal static async ValueTask<IPAddress[]> ResolveCheckedAsync(string host, bool allowPrivateNetworks, CancellationToken ct)
    {
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        if (addresses.Length == 0) throw new HttpRequestException($"The webhook host '{host}' could not be resolved.");
        if (!allowPrivateNetworks && addresses.Any(IsPrivate))
            throw new HttpRequestException($"The webhook host '{host}' resolves to a private, loopback or link-local address; connection refused.");
        return addresses;
    }

    /// <summary>
    /// SocketsHttpHandler.ConnectCallback body: resolves again at connect time (DNS may have changed since the
    /// config was created), checks every address and connects to a checked IP.
    /// </summary>
    internal static async ValueTask<Stream> ConnectCheckedAsync(bool allowPrivateNetworks, DnsEndPoint endpoint, CancellationToken ct)
    {
        var addresses = await ResolveCheckedAsync(endpoint.Host, allowPrivateNetworks, ct).ConfigureAwait(false);
        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw new HttpRequestException($"Could not connect to '{endpoint.Host}'.", last);
    }

    private static A2AException Invalid(string message) => new(A2AErrorCode.InvalidParams, message);
}
