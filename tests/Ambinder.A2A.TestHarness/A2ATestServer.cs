using Ambinder.A2A.Client;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Server.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Ambinder.A2A.Testing;

/// <summary>
/// An A2A server on ASP.NET Core <see cref="TestServer"/>: no ports, a fake clock and an event log.
/// </summary>
/// <remarks>Dispose it at the end of the test. It is safe to use from several threads.</remarks>
public sealed class A2ATestServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly TestServer _server;
    private readonly List<IA2AClient> _clients = [];
    private readonly List<HttpClient> _http = [];

    private A2ATestServer(WebApplication app, FakeTimeProvider time, TaskEventLog events)
    {
        _app = app;
        _server = app.GetTestServer();
        Time = time;
        Events = events;
    }

    /// <summary>The fake clock, registered as the DI <see cref="TimeProvider"/>.</summary>
    public FakeTimeProvider Time { get; }

    /// <summary>Every task event of the server, registered as an <see cref="ITaskEventObserver"/>.</summary>
    public TaskEventLog Events { get; }

    /// <summary>The registered task store.</summary>
    public ITaskStore Store => Services.GetRequiredService<ITaskStore>();

    /// <summary>The server's services.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>The request handler, for in-process calls.</summary>
    public IA2ARequestHandler Handler => Services.GetRequiredService<IA2ARequestHandler>();

    /// <summary>Starts a server.</summary>
    /// <param name="agents">Adds the agents.</param>
    /// <param name="services">Adds or replaces services. It runs before the A2A services are added, so a service registered here wins over a default.</param>
    /// <param name="options">Changes the test server settings.</param>
    /// <returns>The started server.</returns>
    public static async Task<A2ATestServer> StartAsync(
        Action<A2AServerBuilder> agents,
        Action<IServiceCollection>? services = null,
        Action<A2ATestServerOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(agents);
        var o = new A2ATestServerOptions();
        options?.Invoke(o);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = o.Environment });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        o.ConfigureWebHost?.Invoke(builder.WebHost);

        var time = new FakeTimeProvider(o.StartTime);
        var events = new TaskEventLog();
        // Before AddA2AServer, which uses TryAdd, so these are not replaced.
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton(events);
        builder.Services.AddSingleton<ITaskEventObserver>(events);
        services?.Invoke(builder.Services);

        var serverBuilder = builder.Services.AddA2AServer(a => a.PublicBaseUrl = "http://localhost");
        agents(serverBuilder);

        var app = builder.Build();
        o.ConfigureApp?.Invoke(app);
        app.MapA2A();
        await app.StartAsync().ConfigureAwait(false);
        return new A2ATestServer(app, time, events);
    }

    /// <summary>Creates an HTTP client for raw wire tests. The server disposes it.</summary>
    /// <returns>A client with <c>http://localhost</c> as base address.</returns>
    public HttpClient CreateHttpClient()
    {
        var http = _server.CreateClient();
        lock (_http) _http.Add(http);
        return http;
    }

    /// <summary>Creates an SDK client for one agent over one binding. The server disposes it.</summary>
    /// <param name="binding">The binding.</param>
    /// <param name="agent">The agent name. When null, the first registered agent.</param>
    /// <returns>The client.</returns>
    /// <exception cref="NotSupportedException">The binding is not supported by the harness yet.</exception>
    public IA2AClient CreateClient(A2ABinding binding = A2ABinding.JsonRpc, string? agent = null)
    {
        var preferred = binding switch
        {
            A2ABinding.JsonRpc => A2AConstants.BindingJsonRpc,
            A2ABinding.Rest => A2AConstants.BindingRest,
            _ => throw new NotSupportedException($"The test harness does not support the {binding} binding yet."),
        };
        var name = agent ?? Services.GetRequiredService<AgentCatalog>().All.First().Name;
        var http = CreateHttpClient();
        var card = new A2ACardResolver(http, Time)
            .GetAgentCardAsync(new Uri($"http://localhost/agents/{name}/.well-known/agent-card.json"))
            .GetAwaiter().GetResult();
        var client = A2AClientFactory.Create(card, new A2AClientOptions { HttpClient = http, PreferredBinding = preferred });
        lock (_clients) _clients.Add(client);
        return client;
    }

    /// <summary>Stops the host. This runs <c>ApplicationStopping</c> and the hosted services' stop.</summary>
    /// <param name="ct">A token that cancels the stop.</param>
    /// <returns>A task that completes when the host has stopped.</returns>
    public Task StopAsync(CancellationToken ct = default) => _app.StopAsync(ct);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        IA2AClient[] clients;
        lock (_clients) clients = _clients.ToArray();
        foreach (var c in clients) await c.DisposeAsync().ConfigureAwait(false);
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
        lock (_http) foreach (var h in _http) h.Dispose();
    }
}
