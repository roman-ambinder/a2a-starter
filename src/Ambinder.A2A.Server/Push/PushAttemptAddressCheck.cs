using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Server.Push;

/// <summary>
/// The per-attempt SSRF check for proxy mode. The connection goes to the proxy, so the connect-time check cannot see the
/// webhook address. Call <see cref="CheckAsync"/> before every attempt, not only at config create: DNS can change between attempts.
/// Without a proxy it does nothing, because the connect callback of the HTTP client already checks the address.
/// </summary>
internal sealed class PushAttemptAddressCheck
{
    private readonly PushDeliveryMode _mode;
    private readonly PushAddressPolicy _policy;
    private readonly IWebhookAddressResolver _resolver;

    public PushAttemptAddressCheck(PushDeliveryMode mode, IOptions<PushOptions> options, IWebhookAddressResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resolver);
        _mode = mode;
        _policy = new PushAddressPolicy(options.Value);
        _resolver = resolver;
    }

    /// <summary>Resolves the webhook host and applies the address rules of <see cref="PushAddressPolicy"/> to every address.</summary>
    /// <exception cref="HttpRequestException">The host does not resolve, or any address is private, loopback or link-local and not allowed. Nothing was sent.</exception>
    public async ValueTask CheckAsync(Uri target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!_mode.UsesProxy) return;

        IPAddress[] addresses;
        try
        {
            addresses = await _resolver.ResolveAsync(target.DnsSafeHost, ct).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            throw new HttpRequestException($"The webhook host '{target.Host}' could not be resolved.");
        }

        if (addresses.Length == 0)
            throw new HttpRequestException($"The webhook host '{target.Host}' could not be resolved.");
        if (!addresses.All(_policy.IsAllowed))
            throw new HttpRequestException($"The webhook host '{target.Host}' resolves to a private, loopback or link-local address; delivery refused.");
    }
}
