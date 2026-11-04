using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Json;
using Ambinder.A2A.Benchmarks.Agents;
using Ambinder.A2A.Benchmarks.Data;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Auth;
using Ambinder.A2A.Server.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ambinder.A2A.Benchmarks.Hosts;

/// <summary>The transports the Request scenarios run over.</summary>
public enum Transport
{
    JsonRpc,
    Rest,
    Grpc,
}

/// <summary>
/// The A2A app the Request scenarios run against: the real hosting stack (<c>AddA2AServer</c>, <c>MapA2A</c>, and for gRPC
/// <c>AddA2AGrpc</c> and <c>MapA2AGrpc</c>) with <see cref="HelloAgent"/> and <see cref="TaskAgent"/> and a store pre-filled
/// with <see cref="PrefilledTasks"/> tasks. It is built once per benchmark class. <see cref="StartTestServerAsync"/> hosts it in
/// memory (<c>TestServer</c>); <see cref="StartKestrelAsync"/> hosts it on loopback Kestrel.
/// </summary>
/// <remarks>
/// Caller: package 07 has not landed, so today's anonymous owner is used (the tasks are saved with the owner the default resolver
/// gives an anonymous principal). When 07 lands this needs a fixed benchmark caller (task 6, review item 2).
/// gRPC: before package 09, gRPC routes agents by local port, which <c>TestServer</c> does not have. So gRPC is served on Kestrel
/// only; the gRPC server-only rows wait for 09.
/// </remarks>
internal sealed class BenchServer : IAsyncDisposable
{
    public const string HelloAgentName = "hello";
    public const string TaskAgentName = "task";
    public const int PrefilledTasks = 1000;
    public const string KnownTaskId = "bench-task-0";
    public const string HelloText = "Hello from the benchmark agent.";

    private readonly WebApplication _app;

    private BenchServer(WebApplication app, string baseUrl, HttpClient? inMemoryClient)
    {
        _app = app;
        BaseUrl = baseUrl;
        InMemoryClient = inMemoryClient;
    }

    /// <summary>Base URL without a trailing slash (<c>http://localhost</c> for TestServer, <c>http://127.0.0.1:port</c> for Kestrel).</summary>
    public string BaseUrl { get; }

    /// <summary>HTTP client of the in-memory server. Null for Kestrel.</summary>
    public HttpClient? InMemoryClient { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<BenchServer> StartTestServerAsync(Action<WebApplication>? mapExtra = null)
    {
        var builder = CreateBuilder("http://localhost:5200", grpc: false);
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.MapA2A();
        mapExtra?.Invoke(app);
        await app.StartAsync().ConfigureAwait(false);
        await PrefillAsync(app.Services).ConfigureAwait(false);
        var client = app.GetTestClient();
        return new BenchServer(app, "http://localhost", client);
    }

    public static async Task<BenchServer> StartKestrelAsync()
    {
        // The Kestrel ports go into the card (PublicBaseUrl, GrpcPorts), so they are picked before the host is built.
        var httpPort = FreePort();
        var helloGrpc = FreePort();
        var taskGrpc = FreePort();
        var baseUrl = $"http://127.0.0.1:{httpPort}";
        var builder = CreateBuilder(baseUrl, grpc: true, helloGrpc, taskGrpc);
        builder.ConfigureA2AKestrel();
        var app = builder.Build();
        app.MapA2A();
        app.MapA2AGrpc();
        await app.StartAsync().ConfigureAwait(false);
        await PrefillAsync(app.Services).ConfigureAwait(false);
        return new BenchServer(app, baseUrl, null);
    }

    public async ValueTask DisposeAsync()
    {
        InMemoryClient?.Dispose();
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Body of a message request for an agent. <c>TaskAgent</c> reads its number of updates from the text.</summary>
    public static SendMessageRequest MessageRequest(string text, bool returnImmediately = false) => new()
    {
        Message = new Message { MessageId = "bench-message", Role = Role.User, Parts = [Part.FromText(text)] },
        Configuration = returnImmediately ? new SendMessageConfiguration { ReturnImmediately = true } : null,
    };

    public static byte[] JsonRpcBody<T>(string method, T request, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WriteNumber("id", 1);
            w.WriteString("method", method);
            w.WritePropertyName("params");
            JsonSerializer.Serialize(w, request, info);
            w.WriteEndObject();
        }

        return ms.ToArray();
    }

    /// <summary>True when <paramref name="haystack"/> contains the ASCII text <paramref name="needle"/> (no allocation).</summary>
    public static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) => haystack.IndexOf(needle) >= 0;

    private static WebApplicationBuilder CreateBuilder(string publicBaseUrl, bool grpc, int helloGrpc = 0, int taskGrpc = 0)
    {
        // Development: the startup checks for public URL and TLS do not fail the host, and nothing has to be configured.
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);
        var a2a = builder.Services.AddA2AServer(o =>
        {
            o.PublicBaseUrl = publicBaseUrl;
            if (grpc)
            {
                o.GrpcPorts[HelloAgentName] = helloGrpc;
                o.GrpcPorts[TaskAgentName] = taskGrpc;
            }
        });
        if (grpc) a2a.AddA2AGrpc();
        a2a.AddAgent<HelloAgent>(HelloAgentName, _ => Card(HelloAgentName))
            .AddAgent<TaskAgent>(TaskAgentName, _ => Card(TaskAgentName));
        return builder;
    }

    private static AgentCard Card(string name) => new()
    {
        Name = name,
        Description = "Benchmark agent",
        Version = "1.0.0",
        Capabilities = new AgentCapabilities { Streaming = true },
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
        Skills = [new AgentSkill { Id = "bench", Name = "Bench", Description = "Benchmark skill.", Tags = ["bench"] }],
    };

    /// <summary>1,000 completed tasks of the <c>task</c> agent, owned by today's anonymous caller, newest first by index.</summary>
    private static async Task PrefillAsync(IServiceProvider services)
    {
        var store = services.GetRequiredService<ITaskStore>();
        var owner = services.GetRequiredService<IOwnerResolver>().GetOwner(new ClaimsPrincipal(new ClaimsIdentity()));
        var epoch = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < PrefilledTasks; i++)
        {
            var task = Payloads.Task0 with
            {
                Id = "bench-task-" + i.ToString(CultureInfo.InvariantCulture),
                Status = new AgentTaskStatus { State = TaskState.Completed, Timestamp = epoch.AddSeconds(i) },
            };
            await store.SaveAsync(new StoredTask(TaskAgentName, owner, null, task), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
