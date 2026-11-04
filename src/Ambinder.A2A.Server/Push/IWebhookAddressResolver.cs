using System.Net;

namespace Ambinder.A2A.Server.Push;

/// <summary>
/// Resolves a webhook host to addresses for the per-attempt check in proxy mode. A seam so tests can fake DNS.
/// Implementations may be called from many threads at once.
/// </summary>
internal interface IWebhookAddressResolver
{
    /// <summary>Returns every address of <paramref name="host"/>. An IP literal returns itself. Throws <see cref="System.Net.Sockets.SocketException"/> when the name does not resolve.</summary>
    ValueTask<IPAddress[]> ResolveAsync(string host, CancellationToken ct);
}

/// <summary>The default resolver: <see cref="Dns.GetHostAddressesAsync(string, CancellationToken)"/>.</summary>
internal sealed class DnsWebhookAddressResolver : IWebhookAddressResolver
{
    public async ValueTask<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(host);
        return IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
    }
}
