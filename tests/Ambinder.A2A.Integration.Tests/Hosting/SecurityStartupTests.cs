using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Ambinder.A2A.Integration.Tests.Fixtures;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Server.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ambinder.A2A.Integration.Tests.Hosting;

/// <summary>
/// Task 5 (C8): an agent that needs authentication while no scheme is registered fails startup, except in Development for agents
/// with no explicit access mode, which run anonymously with one warning each.
/// </summary>
public sealed class SecurityStartupTests
{
    private const string NoSchemeMessage =
        "Agent 'x' requires authentication but no scheme is registered. Call AddEntraAuthentication(...) or mark the agent AllowAnonymous().";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Builds a host for <paramref name="environment"/> with agents named by <paramref name="agents"/> (access per agent).</summary>
    private static WebApplication Build(string environment, bool withScheme, ConcurrentQueue<(EventId Id, LogLevel Level, string Message)> logs,
        params (string Name, AgentAccess? Access)[] agents)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new CapturingProvider(logs));
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        if (withScheme)
            builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        var a2a = builder.Services.AddA2AServer(o => o.PublicBaseUrl = "http://127.0.0.1:1");
        foreach (var (name, access) in agents)
            a2a.AddAgent<EchoAgent>(name, _ => MinimalServer.Card(name, extended: false), access: access);
        var app = builder.Build();
        if (withScheme) app.UseAuthentication();
        app.MapA2A();
        return app;
    }

    [Fact]
    public async Task Production_without_a_scheme_fails_for_an_agent_with_no_access_mode()
    {
        await using var app = Build("Production", withScheme: false, new(), ("x", null));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(Ct));

        Assert.Equal(NoSchemeMessage, ex.Message);
    }

    [Fact]
    public async Task Production_without_a_scheme_fails_for_an_explicitly_authenticated_agent()
    {
        await using var app = Build("Production", withScheme: false, new(), ("x", AgentAccess.Authenticated));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(Ct));

        Assert.Equal(NoSchemeMessage, ex.Message);
    }

    [Fact]
    public async Task Development_without_a_scheme_still_fails_for_an_explicitly_authenticated_agent()
    {
        await using var app = Build("Development", withScheme: false, new(), ("x", AgentAccess.Authenticated));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(Ct));

        Assert.Equal(NoSchemeMessage, ex.Message);
    }

    [Fact]
    public async Task Production_without_a_scheme_starts_when_every_agent_allows_anonymous()
    {
        var logs = new ConcurrentQueue<(EventId, LogLevel, string)>();
        await using var app = Build("Production", withScheme: false, logs, ("x", AgentAccess.Anonymous));

        await app.StartAsync(Ct);
        await app.StopAsync(Ct);

        Assert.DoesNotContain(logs, l => l.Item2 >= LogLevel.Warning);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task A_registered_scheme_satisfies_the_check_and_the_agent_stays_authenticated(string environment)
    {
        var logs = new ConcurrentQueue<(EventId, LogLevel, string)>();
        await using var app = Build(environment, withScheme: true, logs, ("x", null));
        await app.StartAsync(Ct);

        var status = await PostSendAsync(app, "x");

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.DoesNotContain(logs, l => l.Item2 >= LogLevel.Warning);
    }

    [Fact]
    public async Task Development_without_a_scheme_runs_agents_with_no_access_mode_anonymously_and_warns_once_per_agent()
    {
        var logs = new ConcurrentQueue<(EventId Id, LogLevel Level, string Message)>();
        await using var app = Build("Development", withScheme: false, logs, ("x", null), ("y", null), ("z", AgentAccess.Anonymous));
        await app.StartAsync(Ct);

        var status = await PostSendAsync(app, "x");

        Assert.Equal(HttpStatusCode.OK, status);
        var warnings = logs.Where(l => l.Level == LogLevel.Warning).ToList();
        Assert.Equal(2, warnings.Count); // x and y; z chose its mode
        Assert.All(warnings, w => Assert.Equal(2010, w.Id.Id));
        Assert.Contains(warnings, w => w.Message.Contains("'x'", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Message.Contains("'y'", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, w => w.Message.Contains("'z'", StringComparison.Ordinal));
    }

    private static async Task<HttpStatusCode> PostSendAsync(WebApplication app, string agent)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{address}/agents/{agent}/jsonrpc")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"messageId":"m","role":"ROLE_USER","parts":[{"text":"hi"}]}}}""",
                Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(A2AConstants.VersionHeader, "1.0");
        using var response = await http.SendAsync(request, Ct);
        return response.StatusCode;
    }

    private sealed class CapturingProvider(ConcurrentQueue<(EventId Id, LogLevel Level, string Message)> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(logs);
        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<(EventId Id, LogLevel Level, string Message)> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => logs.Enqueue((eventId, logLevel, formatter(state, exception)));
        }
    }
}
