using System.Net;
using Ambinder.A2A.AspNetCore.Push;
using Ambinder.A2A.Server.Push;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.AspNetCore.Tests.Push;

public sealed class PushHttpClientTests
{
    private static SocketsHttpHandler Handler(Action<PushOptions>? configure = null)
    {
        var options = new PushOptions();
        configure?.Invoke(options);
        return PushHttpClientSetup.CreateHandler(options);
    }

    [Fact]
    public async Task No_proxy_path_checks_address_at_connect()
    {
        using var handler = Handler();
        Assert.False(handler.UseProxy);
        Assert.NotNull(handler.ConnectCallback);

        // The connect callback runs before any socket is opened, so a loopback target is refused without network I/O.
        using var client = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("http://127.0.0.1:9/hook", TestContext.Current.CancellationToken));
        Assert.Contains("private, loopback or link-local", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Redirects_not_followed_and_cookies_off()
    {
        foreach (var handler in new[] { Handler(), Handler(o => o.Proxy = new Uri("http://proxy.contoso.com:3128")), Handler(o => o.UseSystemProxy = true) })
        {
            using (handler)
            {
                Assert.False(handler.AllowAutoRedirect);
                Assert.False(handler.UseCookies);
            }
        }

        // A 302 is a non-2xx answer; the delivery path settles it by status class.
        using var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        Assert.False(redirect.IsSuccessStatusCode);
    }

    [Fact]
    public void Proxy_mode_uses_proxy_without_connect_check_and_keeps_credentials_out_of_the_address()
    {
        using var handler = Handler(o => o.Proxy = new Uri("http://user:p%40ss@proxy.contoso.com:3128/"));
        Assert.True(handler.UseProxy);
        Assert.Null(handler.ConnectCallback); // it would see the proxy, not the webhook host
        var proxy = Assert.IsType<WebProxy>(handler.Proxy);
        Assert.Equal(new Uri("http://proxy.contoso.com:3128/"), proxy.Address);
        var credential = Assert.IsType<NetworkCredential>(proxy.Credentials);
        Assert.Equal("user", credential.UserName);
        Assert.Equal("p@ss", credential.Password);
    }

    [Fact]
    public void Proxy_without_user_info_uses_default_credentials()
    {
        using var handler = Handler(o => o.Proxy = new Uri("http://proxy.contoso.com:3128"));
        var proxy = Assert.IsType<WebProxy>(handler.Proxy);
        Assert.Same(CredentialCache.DefaultCredentials, proxy.Credentials);
    }

    [Fact]
    public void System_proxy_mode_uses_the_system_proxy_without_connect_check()
    {
        using var handler = Handler(o => o.UseSystemProxy = true);
        Assert.True(handler.UseProxy);
        Assert.Null(handler.Proxy);
        Assert.Null(handler.ConnectCallback);
    }

    [Fact]
    public void AddA2AServer_registers_the_named_client_with_the_aspnetcore_handler()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddA2AServer(o => o.PublicBaseUrl = "http://localhost:5200");
        using var provider = services.BuildServiceProvider();

        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(PushHttpClientSetup.HttpClientName);
        while (handler is DelegatingHandler d) handler = d.InnerHandler;

        var sockets = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(sockets.UseProxy);
        Assert.False(sockets.AllowAutoRedirect);
        Assert.NotNull(sockets.ConnectCallback);
    }
}
