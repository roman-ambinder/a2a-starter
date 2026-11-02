using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using A2A.Protocol;
using A2A.Server.Agents;
using A2A.Server.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace A2A.Integration.Tests.Fixtures;

/// <summary>
/// The real A2A hosting stack on real Kestrel, with small test agents. One instance per test run (collection fixture).
/// Ports: base (env <c>A2A_TEST_PORT_BASE</c>, default 16000) + 200 = HTTP; + 201 / + 202 are the
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

    public static int PortBase { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("A2A_TEST_PORT_BASE"), out var b) ? b : DefaultPortBase;

    public static int HttpPort => PortBase + 200;
    public static int EchoGrpcPort => PortBase + 201;
    public static int ProtectedGrpcPort => PortBase + 202;

    /// <summary>HTTP/2 listener that no agent owns (gRPC call there → NOT_FOUND).</summary>
    public static int UnmappedGrpcPort => PortBase + 203;

    /// <summary>Base URL of the HTTP listener (cards, JSON-RPC, REST), without a trailing slash.</summary>
    public static string BaseUrl => $"http://127.0.0.1:{HttpPort}";

    private WebApplication? _app;

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

[CollectionDefinition(Name)]
public sealed class MinimalServerCollection : ICollectionFixture<MinimalServer>
{
    public const string Name = "MinimalServer";
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
