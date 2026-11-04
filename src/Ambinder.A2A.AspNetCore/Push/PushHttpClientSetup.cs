using System.Net;
using Ambinder.A2A.Server.Push;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.AspNetCore.Push;

/// <summary>
/// The primary handler of the <c>"a2a-push"</c> named HTTP client. The core registers the plain client; this adds the host
/// parts. Without a proxy it checks the address at connect time. With a proxy the connect-time check would see the proxy, so the
/// delivery path checks the webhook address before every attempt instead (<c>PushAttemptAddressCheck</c> in the core).
/// Redirects and cookies are always off.
/// </summary>
internal static class PushHttpClientSetup
{
    /// <summary>The client name package 12's harness replaces the handler of. Must equal the name the core registers.</summary>
    internal const string HttpClientName = "a2a-push";

    internal static IServiceCollection AddA2APushHttpClient(this IServiceCollection services)
    {
        services.AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(sp => CreateHandler(sp.GetRequiredService<IOptions<PushOptions>>().Value));
        return services;
    }

    internal static SocketsHttpHandler CreateHandler(PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        };

        if (options.Proxy is { IsAbsoluteUri: true } proxy)
        {
            handler.UseProxy = true;
            handler.Proxy = CreateProxy(proxy);
        }
        else if (options.UseSystemProxy)
        {
            handler.UseProxy = true; // Proxy stays null: HttpClient.DefaultProxy (environment variables and system settings)
        }
        else
        {
            // Checked again at connect time, so DNS rebinding after config creation cannot reach private addresses.
            var policy = new PushAddressPolicy(options);
            handler.UseProxy = false;
            handler.ConnectCallback = (context, ct) => WebhookUrlValidator.ConnectCheckedAsync(policy, context.DnsEndPoint, ct);
        }

        return handler;
    }

    private static WebProxy CreateProxy(Uri proxy)
    {
        // The address without user info, so the credentials live only in the credential object.
        var address = new UriBuilder(proxy.Scheme, proxy.Host, proxy.Port).Uri;
        var credentials = string.IsNullOrEmpty(proxy.UserInfo)
            ? CredentialCache.DefaultCredentials
            : ParseCredentials(proxy.UserInfo);
        return new WebProxy(address) { Credentials = credentials };
    }

    private static NetworkCredential ParseCredentials(string userInfo)
    {
        var colon = userInfo.IndexOf(':', StringComparison.Ordinal);
        var user = Uri.UnescapeDataString(colon < 0 ? userInfo : userInfo[..colon]);
        var password = colon < 0 ? string.Empty : Uri.UnescapeDataString(userInfo[(colon + 1)..]);
        return new NetworkCredential(user, password);
    }
}
