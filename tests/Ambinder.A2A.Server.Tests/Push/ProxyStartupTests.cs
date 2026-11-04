using System.Net;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Server.Push;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Server.Tests.Push;

public sealed class ProxyStartupTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Collector : ILoggerProvider, ILogger
    {
        public List<(LogLevel Level, EventId Id, string Message)> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, eventId, formatter(state, exception)));
    }

    private sealed class FakeResolver(params string[] addresses) : IWebhookAddressResolver
    {
        public int Calls;
        public ValueTask<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return ValueTask.FromResult(addresses.Select(IPAddress.Parse).ToArray());
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static async Task<Collector> StartAsync(string environment, Action<PushOptions> configure)
    {
        var logs = new Collector();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new Env(environment));
        services.AddLogging(b => b.AddProvider(logs));
        services.AddA2AServerCore(o => { o.PublicBaseUrl = "http://localhost:5200"; configure(o.Push); });
        await using var provider = services.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        return logs;
    }

    [Fact]
    public async Task Proxy_requires_ProxyEnforcesEgressPolicy_outside_Development()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StartAsync(Environments.Production, o => o.Proxy = new Uri("http://proxy.contoso.com:3128")));
        Assert.Equal(
            "A2A:Push:Proxy is set, but the proxy is not declared to block private networks. Set A2A:Push:ProxyEnforcesEgressPolicy=true once the proxy enforces egress rules, or remove the proxy.",
            ex.Message);
    }

    [Fact]
    public async Task System_proxy_also_requires_the_flag_outside_Development()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(Environments.Production, o => o.UseSystemProxy = true));

    [Fact]
    public async Task Proxy_allowed_outside_Development_when_declared()
        => await StartAsync(Environments.Production, o => { o.Proxy = new Uri("http://proxy.contoso.com:3128"); o.ProxyEnforcesEgressPolicy = true; });

    [Fact]
    public async Task Proxy_allowed_in_Development_without_flag()
        => await StartAsync(Environments.Development, o => o.Proxy = new Uri("http://proxy.contoso.com:3128"));

    [Fact]
    public async Task No_proxy_starts_without_flag_in_Production()
        => await StartAsync(Environments.Production, _ => { });

    [Fact]
    public async Task Proxy_credentials_not_logged()
    {
        var logs = await StartAsync(Environments.Development, o => o.Proxy = new Uri("http://alice:s3cr3t-pw@proxy.contoso.com:3128/path?token=abc"));
        var entry = Assert.Single(logs.Entries, e => e.Id.Id == 1107);
        Assert.Contains("http://proxy.contoso.com:3128", entry.Message, StringComparison.Ordinal);
        foreach (var (_, _, message) in logs.Entries)
        {
            Assert.DoesNotContain("s3cr3t-pw", message, StringComparison.Ordinal);
            Assert.DoesNotContain("alice", message, StringComparison.Ordinal);
            Assert.DoesNotContain("token=abc", message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Core_has_no_SocketsHttpHandler_setup()
    {
        // A server-only host: the named client has the default handlers, no connect callback and no host code.
        using var provider = new ServiceCollection().AddLogging().AddA2APush().BuildServiceProvider();
        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(PushNotificationSender.HttpClientName);
        while (handler is DelegatingHandler d) handler = d.InnerHandler;

        // .NET 10's default primary handler is itself a SocketsHttpHandler; the core must leave it untouched.
        var sockets = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.Null(sockets.ConnectCallback);
        Assert.True(sockets.UseProxy);
        Assert.True(sockets.AllowAutoRedirect);
        Assert.True(sockets.UseCookies);
        Assert.Equal(System.Threading.Timeout.InfiniteTimeSpan, provider.GetRequiredService<IHttpClientFactory>().CreateClient(PushNotificationSender.HttpClientName).Timeout);
    }

    private static PushAttemptAddressCheck Check(Action<PushOptions> configure, IWebhookAddressResolver resolver)
    {
        var options = new PushOptions();
        configure(options);
        return new PushAttemptAddressCheck(new PushDeliveryMode(Options.Create(options)), Options.Create(options), resolver);
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    public async Task Proxy_mode_refuses_private_target_before_send(string address)
    {
        var proxy = new CountingHandler();
        var resolver = new FakeResolver(address);
        var check = Check(o => o.Proxy = new Uri("http://proxy.contoso.com:3128"), resolver);
        using var client = new HttpClient(proxy);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            var target = new Uri("https://hooks.contoso.com/a2a");
            await check.CheckAsync(target, TestContext.Current.CancellationToken);
            await client.PostAsync(target, new StringContent("{}"), TestContext.Current.CancellationToken);
        });
        Assert.Equal(0, proxy.Requests);
    }

    [Fact]
    public async Task Proxy_mode_refuses_when_any_resolved_address_is_private()
    {
        var check = Check(o => o.Proxy = new Uri("http://proxy.contoso.com:3128"), new FakeResolver("93.184.216.34", "10.0.0.5"));
        await Assert.ThrowsAsync<HttpRequestException>(() => check.CheckAsync(new Uri("https://hooks.contoso.com/a2a"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Proxy_mode_checks_on_every_attempt_not_only_the_first()
    {
        // DNS rebinding between attempts: the first answer is public, the second is private.
        var answers = new Queue<string>(["93.184.216.34", "10.0.0.5"]);
        var check = Check(o => o.Proxy = new Uri("http://proxy.contoso.com:3128"), new SequenceResolver(answers));
        var target = new Uri("https://hooks.contoso.com/a2a");

        await check.CheckAsync(target, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<HttpRequestException>(() => check.CheckAsync(target, TestContext.Current.CancellationToken).AsTask());
    }

    private sealed class SequenceResolver(Queue<string> answers) : IWebhookAddressResolver
    {
        public ValueTask<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => ValueTask.FromResult(new[] { IPAddress.Parse(answers.Dequeue()) });
    }

    [Fact]
    public async Task Proxy_mode_allows_public_and_AllowedNetworks_targets()
    {
        await Check(o => o.Proxy = new Uri("http://proxy.contoso.com:3128"), new FakeResolver("93.184.216.34"))
            .CheckAsync(new Uri("https://hooks.contoso.com/a2a"), TestContext.Current.CancellationToken);
        await Check(o => { o.Proxy = new Uri("http://proxy.contoso.com:3128"); o.AllowedNetworks.Add("10.1.0.0/16"); }, new FakeResolver("10.1.2.3"))
            .CheckAsync(new Uri("https://hooks.contoso.com/a2a"), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Without_proxy_the_check_does_nothing_because_the_connect_callback_checks()
    {
        var resolver = new FakeResolver("10.0.0.5");
        await Check(_ => { }, resolver).CheckAsync(new Uri("https://hooks.contoso.com/a2a"), TestContext.Current.CancellationToken);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task Unresolvable_host_is_refused_in_proxy_mode()
    {
        var check = Check(o => o.Proxy = new Uri("http://proxy.contoso.com:3128"), new FakeResolver());
        await Assert.ThrowsAsync<HttpRequestException>(() => check.CheckAsync(new Uri("https://hooks.contoso.com/a2a"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public void Delivery_mode_describes_the_proxy_without_credentials()
    {
        var mode = new PushDeliveryMode(Options.Create(new PushOptions { Proxy = new Uri("http://alice:pw@proxy.contoso.com:3128/x?y=1") }));
        Assert.True(mode.UsesProxy);
        Assert.Equal("http://proxy.contoso.com:3128", mode.ProxyDescription);
        Assert.False(new PushDeliveryMode(Options.Create(new PushOptions())).UsesProxy);
        Assert.True(new PushDeliveryMode(Options.Create(new PushOptions { UseSystemProxy = true })).UsesProxy);
    }
}
