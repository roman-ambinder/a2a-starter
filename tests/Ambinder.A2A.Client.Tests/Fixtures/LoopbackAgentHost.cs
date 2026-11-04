using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Ambinder.A2A.Server;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Client.Tests.Fixtures;

/// <summary>
/// The real A2A server on Kestrel with no fixed ports. One HTTP/1.1 listener on a free loopback port serves the cards, JSON-RPC and REST.
/// Every agent that the test registers also gets its own HTTP/2 (h2c) listener for gRPC, because the server
/// maps a gRPC call to an agent by local port. The ports are free ephemeral ports that are reserved at start (the server needs them
/// for its card URLs before it binds), so no test depends on a fixed port.
/// </summary>
public sealed class LoopbackAgentHost : IAsyncDisposable
{
    private readonly ConcurrentDictionary<HttpContext, byte> _live = new();
    private WebApplication? _app;
    private int _connections;

    private LoopbackAgentHost() { }

    /// <summary>Base URL of the HTTP/1.1 listener, without a trailing slash.</summary>
    public string BaseUrl { get; private set; } = "";

    /// <summary>The h2c port of each agent (agent name to port).</summary>
    public IReadOnlyDictionary<string, int> GrpcPorts { get; private set; } = new Dictionary<string, int>();

    /// <summary>The server's services.</summary>
    public IServiceProvider Services => _app!.Services;

    /// <summary>How many connections the HTTP/1.1 listener has accepted.</summary>
    public int ConnectionCount => Volatile.Read(ref _connections);

    /// <summary>The URL of the agent's JSON-RPC endpoint.</summary>
    public string JsonRpcUrl(string agent) => $"{BaseUrl}/agents/{agent}/jsonrpc";

    /// <summary>The URL of the agent's REST endpoint.</summary>
    public string RestUrl(string agent) => $"{BaseUrl}/agents/{agent}/rest";

    /// <summary>The URL of the agent's card.</summary>
    public string CardUrl(string agent) => $"{BaseUrl}/agents/{agent}/.well-known/agent-card.json";

    /// <summary>The gRPC target (<c>host:port</c>) of the agent.</summary>
    public string GrpcTarget(string agent) => $"127.0.0.1:{GrpcPorts[agent]}";

    /// <summary>Aborts every request that is in flight now. The clients see a broken connection or stream.</summary>
    public void AbortInFlightRequests()
    {
        foreach (var context in _live.Keys) context.Abort();
    }

    /// <summary>
    /// Starts the host. <paramref name="agents"/> registers the test agents (<c>AddAgent</c>). The first agent is the default agent.
    /// <paramref name="listen"/> runs on the HTTP/1.1 listener. <c>AddA2AGrpc</c> is registered for you.
    /// </summary>
    /// <remarks>
    /// The <paramref name="agents"/> callback runs twice (once to learn the agent names, once on the real server), and so it
    /// must be free of side effects. If Kestrel cannot bind a reserved port because another process took it, the start is retried once.
    /// </remarks>
    public static async Task<LoopbackAgentHost> StartAsync(Action<A2AServerBuilder> agents, Action<ListenOptions>? listen = null)
    {
        try
        {
            return await StartOnceAsync(agents, listen);
        }
        catch (Exception ex) when (IsAddressInUse(ex))
        {
            return await StartOnceAsync(agents, listen);
        }
    }

    private static bool IsAddressInUse(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is AddressInUseException) return true;
        return false;
    }

    private static async Task<LoopbackAgentHost> StartOnceAsync(Action<A2AServerBuilder> agents, Action<ListenOptions>? listen)
    {
        ArgumentNullException.ThrowIfNull(agents);
        // The server freezes the card URLs when it builds its agent catalog, which happens before Kestrel binds. So the ports must
        // be known first: reserve free ephemeral ports (never a fixed number), then release them just before Kestrel binds.
        string[] names;
        {
            var probeServices = new ServiceCollection().AddLogging();
            agents(probeServices.AddA2AServer(o => o.PublicBaseUrl = "http://127.0.0.1:1"));
            using var provider = probeServices.BuildServiceProvider();
            names = provider.GetRequiredService<AgentCatalog>().All.Select(a => a.Name).ToArray();
        }
        if (names.Length == 0) throw new InvalidOperationException("Register at least one agent.");
        var (httpPort, agentPorts) = ReservePorts(names);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        var server = builder.Services.AddA2AServer(o =>
        {
            o.PublicBaseUrl = $"http://127.0.0.1:{httpPort}";
            o.DefaultAgent = names[0];
            o.Push.AllowPrivateNetworks = true;
            foreach (var (name, port) in agentPorts) o.GrpcPorts[name] = port;
        });
        server.AddA2AGrpc();
        agents(server);

        var host = new LoopbackAgentHost();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, httpPort, o =>
            {
                o.Protocols = HttpProtocols.Http1;
                o.Use(next => context =>
                {
                    Interlocked.Increment(ref host._connections);
                    return next(context);
                });
                listen?.Invoke(o);
            });
            foreach (var port in agentPorts.Values)
                kestrel.Listen(IPAddress.Loopback, port, o => o.Protocols = HttpProtocols.Http2);
        });

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            host._live.TryAdd(context, 0);
            try { await next(context); }
            finally { host._live.TryRemove(context, out _); }
        });
        app.MapA2A();
        app.MapA2AGrpc();
        await app.StartAsync();

        var baseUrl = $"http://127.0.0.1:{httpPort}";
        host._app = app;
        host.BaseUrl = baseUrl;
        host.GrpcPorts = agentPorts;
        return host;
    }

    private static (int Http, Dictionary<string, int> Agents) ReservePorts(string[] names)
    {
        var listeners = new List<TcpListener>();
        try
        {
            for (var i = 0; i <= names.Length; i++)
            {
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                listeners.Add(l);
            }
            var ports = listeners.Select(l => ((IPEndPoint)l.LocalEndpoint).Port).ToArray();
            return (ports[0], names.Select((n, i) => (n, ports[i + 1])).ToDictionary(x => x.n, x => x.Item2, StringComparer.Ordinal));
        }
        finally
        {
            foreach (var l in listeners) l.Stop();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_app is null) return;
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
