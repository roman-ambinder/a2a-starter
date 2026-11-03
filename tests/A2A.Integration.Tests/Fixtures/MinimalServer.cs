using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using A2A.Protocol;
using A2A.Server;
using A2A.Server.Agents;
using A2A.Server.Hosting;
using A2A.Server.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace A2A.Integration.Tests.Fixtures;

/// <summary>
/// The real A2A hosting stack on real Kestrel, with small test agents. One instance per test run (collection fixture).
/// Ports: base (env <c>A2A_TEST_PORT_BASE</c> + 1000, default 16000) + 200 = HTTP; + 201 / + 202 are the
/// h2c gRPC listeners of <see cref="EchoAgentName"/> / <see cref="ProtectedAgentName"/>; + 203 is an h2c listener no agent owns.
/// </summary>
public sealed class MinimalServer : IAsyncLifetime
{
    public const string EchoAgentName = "echo";
    public const string ProtectedAgentName = "protected";
    public const string InvokerPolicy = "RequireInvoker";
    public const string InvokerRole = "A2A.Invoke";
    public const string UserHeader = TestAuthHandler.UserHeader;
    public const string RolesHeader = TestAuthHandler.RolesHeader;
    public const int DefaultPortBase = 16000;

    /// <summary>
    /// <c>A2A_TEST_PORT_BASE</c> + 1000 (default 15000 + 1000 = 16000), so it never overlaps <see cref="A2ATestHost"/> (base + 0)
    /// when the variable is set.
    /// </summary>
    public static int PortBase { get; } =
        (int.TryParse(Environment.GetEnvironmentVariable("A2A_TEST_PORT_BASE"), out var b) ? b : A2ATestHost.DefaultPortBase) + 1000;

    public static int HttpPort => PortBase + 200;
    public static int EchoGrpcPort => PortBase + 201;
    public static int ProtectedGrpcPort => PortBase + 202;

    /// <summary>HTTP/2 listener that no agent owns (gRPC call there → NOT_FOUND).</summary>
    public static int UnmappedGrpcPort => PortBase + 203;

    /// <summary>Base URL of the HTTP listener (cards, JSON-RPC, REST), without a trailing slash.</summary>
    public static string BaseUrl => $"http://127.0.0.1:{HttpPort}";

    private WebApplication? _app;

    /// <summary>Every call the transports made into the request handler: operation name and <see cref="A2ACallContext.Tenant"/>.</summary>
    public ConcurrentQueue<(string Operation, string? Tenant)> HandlerCalls { get; } = new();

    /// <summary>The server's clock. Follows the system clock until a test calls <see cref="TestClock.Freeze"/>.</summary>
    public TestClock Clock { get; } = new();

    public WebApplication App => _app ?? throw new InvalidOperationException("Server not started.");

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        // HTTP port + the two agent gRPC ports come from the A2A options (PublicBaseUrl, GrpcPorts).
        builder.ConfigureA2AKestrel();
        // One more h2c listener that no agent owns, for the "unknown port -> NOT_FOUND" test.
        builder.WebHost.ConfigureKestrel(k => k.Listen(System.Net.IPAddress.Loopback, UnmappedGrpcPort,
            l => l.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2));

        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(InvokerPolicy, p => p.RequireAuthenticatedUser().RequireRole(InvokerRole));

        builder.Services.AddSingleton<TimeProvider>(Clock);

        // Registered before AddA2AServer (which uses TryAdd), so the transports call the recorder, which calls TaskManager.
        builder.Services.AddSingleton<IA2ARequestHandler>(sp => new RecordingHandler(sp.GetRequiredService<TaskManager>(), HandlerCalls));
        builder.Services
            .AddA2AServer(o =>
            {
                o.PublicBaseUrl = BaseUrl;
                o.DefaultAgent = EchoAgentName;
                o.GrpcPorts[EchoAgentName] = EchoGrpcPort;
                o.GrpcPorts[ProtectedAgentName] = ProtectedGrpcPort;
                o.Push.AllowPrivateNetworks = true;
            })
            .AddAgent<EchoAgent>(EchoAgentName, _ => Card(EchoAgentName, extended: true), _ => Card(EchoAgentName, extended: true) with
            {
                Description = "Extended echo card",
            })
            .AddAgent<EchoAgent>(ProtectedAgentName, _ => Card(ProtectedAgentName, extended: false), authorizationPolicy: InvokerPolicy);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.MapA2A();
        _app.MapA2AGrpc();
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>An HttpClient for <see cref="BaseUrl"/>. Optionally signs in as a test user.</summary>
    public HttpClient CreateClient(string? user = null, params string[] roles)
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseUrl + "/"), Timeout = TimeSpan.FromSeconds(30) };
        if (user is not null) client.DefaultRequestHeaders.Add(UserHeader, user);
        if (roles.Length > 0) client.DefaultRequestHeaders.Add(RolesHeader, string.Join(',', roles));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public static AgentCard Card(string name, bool extended) => new()
    {
        Name = name,
        Description = $"Test agent {name}",
        Version = "1.0.0",
        Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = true, ExtendedAgentCard = extended },
        DefaultInputModes = ["text/plain", "application/json"],
        DefaultOutputModes = ["text/plain"],
        Skills = [new AgentSkill { Id = "echo", Name = "Echo", Description = "Echoes the text back." }],
    };
}

/// <summary>A clock that reads the system time until a test freezes it at an instant of its choice.</summary>
public sealed class TestClock : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset? _frozen;

    /// <summary>Makes <see cref="GetUtcNow"/> return <paramref name="instant"/> until <see cref="Release"/>.</summary>
    public void Freeze(DateTimeOffset instant) { lock (_gate) _frozen = instant; }

    /// <summary>Back to the system clock.</summary>
    public void Release() { lock (_gate) _frozen = null; }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _frozen ?? base.GetUtcNow();
    }
}

[CollectionDefinition(Name)]
public sealed class MinimalServerCollection : ICollectionFixture<MinimalServer>
{
    public const string Name = "MinimalServer";
}

/// <summary>Records operation name and tenant of each call, then delegates to the real handler.</summary>
public sealed class RecordingHandler(IA2ARequestHandler inner, ConcurrentQueue<(string Operation, string? Tenant)> calls) : IA2ARequestHandler
{
    private T Record<T>(string operation, A2ACallContext call, T result)
    {
        calls.Enqueue((operation, call.Tenant));
        return result;
    }

    public Task<SendMessageResponse> SendMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct)
        => Record("SendMessage", call, inner.SendMessageAsync(call, request, ct));
    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct)
        => Record("SendStreamingMessage", call, inner.SendStreamingMessageAsync(call, request, ct));
    public Task<AgentTask> GetTaskAsync(A2ACallContext call, GetTaskRequest request, CancellationToken ct)
        => Record("GetTask", call, inner.GetTaskAsync(call, request, ct));
    public Task<ListTasksResponse> ListTasksAsync(A2ACallContext call, ListTasksRequest request, CancellationToken ct)
        => Record("ListTasks", call, inner.ListTasksAsync(call, request, ct));
    public Task<AgentTask> CancelTaskAsync(A2ACallContext call, CancelTaskRequest request, CancellationToken ct)
        => Record("CancelTask", call, inner.CancelTaskAsync(call, request, ct));
    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(A2ACallContext call, SubscribeToTaskRequest request, CancellationToken ct)
        => Record("SubscribeToTask", call, inner.SubscribeToTaskAsync(call, request, ct));
    public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(A2ACallContext call, TaskPushNotificationConfig request, CancellationToken ct)
        => Record("CreateTaskPushNotificationConfig", call, inner.CreateTaskPushNotificationConfigAsync(call, request, ct));
    public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(A2ACallContext call, GetTaskPushNotificationConfigRequest request, CancellationToken ct)
        => Record("GetTaskPushNotificationConfig", call, inner.GetTaskPushNotificationConfigAsync(call, request, ct));
    public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(A2ACallContext call, ListTaskPushNotificationConfigsRequest request, CancellationToken ct)
        => Record("ListTaskPushNotificationConfigs", call, inner.ListTaskPushNotificationConfigsAsync(call, request, ct));
    public Task DeleteTaskPushNotificationConfigAsync(A2ACallContext call, DeleteTaskPushNotificationConfigRequest request, CancellationToken ct)
        => Record("DeleteTaskPushNotificationConfig", call, inner.DeleteTaskPushNotificationConfigAsync(call, request, ct));
    public Task<AgentCard> GetExtendedAgentCardAsync(A2ACallContext call, GetExtendedAgentCardRequest request, CancellationToken ct)
        => Record("GetExtendedAgentCard", call, inner.GetExtendedAgentCardAsync(call, request, ct));
}

/// <summary>WORKING → artifact "echo: {text}" → COMPLETED. Text <c>slow:{ms}</c> waits that long after WORKING.</summary>
public sealed class EchoAgent : IAgentHandler
{
    public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        var text = string.Concat(context.Message.Parts.Select(p => p.Text));
        await sink.UpdateStatusAsync(TaskState.Working, null, ct);
        if (text.StartsWith("slow:", StringComparison.Ordinal) && int.TryParse(text["slow:".Length..], out var ms))
            await Task.Delay(ms, ct);
        await sink.AddArtifactAsync(new Artifact { ArtifactId = "echo", Parts = [Part.FromText($"echo: {text}")] }, append: false, lastChunk: true, ct);
        await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
    }
}

/// <summary>
/// Test scheme: <c>X-Test-User: name</c> → authenticated principal with <c>sub</c> = name (no <c>tid</c>);
/// <c>X-Test-Roles: a,b</c> → role claims. No header → no result (anonymous).
/// </summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers[UserHeader].ToString();
        if (user.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("sub", user), new(ClaimTypes.Name, user) };
        foreach (var role in Request.Headers[RolesHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            claims.Add(new Claim(ClaimTypes.Role, role));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
