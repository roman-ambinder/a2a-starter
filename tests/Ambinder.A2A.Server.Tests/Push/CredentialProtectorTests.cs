using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Server.Push;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ambinder.A2A.Server.Tests.Push;

public sealed class CredentialProtectorTests
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

    private static async Task<Collector> StartAsync(string environment, Action<IServiceCollection>? configure = null)
    {
        var logs = new Collector();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new Env(environment));
        services.AddLogging(b => b.AddProvider(logs));
        services.AddA2AServerCore(o => o.PublicBaseUrl = "http://localhost:5200");
        configure?.Invoke(services);
        await using var provider = services.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        return logs;
    }

    [Fact]
    public void PassThrough_returns_input()
    {
        var p = new PassThroughPushCredentialProtector();
        Assert.Equal("secret", p.Protect("secret"));
        Assert.Equal("secret", p.Unprotect("secret"));
    }

    [Fact]
    public async Task PassThrough_warns_outside_Development()
    {
        var logs = await StartAsync(Environments.Production);
        var warnings = logs.Entries.Where(e => e.Id.Id == 1106).ToList();
        var single = Assert.Single(warnings);
        Assert.Equal(LogLevel.Warning, single.Level);
        Assert.Equal("Push credentials are stored unprotected. Reference Ambinder.A2A.AspNetCore (Data Protection) or register an IPushCredentialProtector.", single.Message);
    }

    [Fact]
    public async Task PassThrough_silent_in_Development()
    {
        var logs = await StartAsync(Environments.Development);
        Assert.DoesNotContain(logs.Entries, e => e.Id.Id == 1106);
    }

    [Fact]
    public async Task Custom_protector_is_silent_outside_Development()
    {
        var logs = await StartAsync(Environments.Production, s => s.AddSingleton<IPushCredentialProtector, Custom>());
        Assert.DoesNotContain(logs.Entries, e => e.Id.Id == 1106);
    }

    [Fact]
    public void Default_request_authenticator_is_a_no_op_and_can_be_replaced()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddA2AServerCore(o => o.PublicBaseUrl = "http://localhost:5200");
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IPushRequestAuthenticator>());

        var custom = new ServiceCollection();
        custom.AddLogging();
        custom.AddSingleton<IPushRequestAuthenticator, CustomAuth>();
        custom.AddA2AServerCore(o => o.PublicBaseUrl = "http://localhost:5200");
        using var customProvider = custom.BuildServiceProvider();
        Assert.IsType<CustomAuth>(customProvider.GetRequiredService<IPushRequestAuthenticator>());
    }

    [Fact]
    public async Task Default_request_authenticator_leaves_the_request_unchanged()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddA2AServerCore(o => o.PublicBaseUrl = "http://localhost:5200");
        using var provider = services.BuildServiceProvider();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/hook");
        await provider.GetRequiredService<IPushRequestAuthenticator>()
            .AuthenticateAsync(request, new Ambinder.A2A.Protocol.TaskPushNotificationConfig { Url = "https://example.test/hook" }, CancellationToken.None);
        Assert.Empty(request.Headers);
    }

    private sealed class Custom : IPushCredentialProtector
    {
        public string Protect(string plaintext) => "p:" + plaintext;
        public string Unprotect(string protectedValue) => protectedValue[2..];
    }

    private sealed class CustomAuth : IPushRequestAuthenticator
    {
        public ValueTask AuthenticateAsync(HttpRequestMessage request, Ambinder.A2A.Protocol.TaskPushNotificationConfig config, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
