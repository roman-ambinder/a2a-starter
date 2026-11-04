using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Ambinder.A2A.AspNetCore.Grpc;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Interop.Tests.Agent;

/// <summary>Settings of <see cref="InteropServerHost"/>.</summary>
public sealed record InteropHostOptions
{
    /// <summary>Host in the public URLs (cards, interfaces). Default <c>127.0.0.1</c>; a container run uses <c>host.testcontainers.internal</c>.</summary>
    public string PublicHost { get; init; } = "127.0.0.1";
}

/// <summary>
/// Our interop server, in process on real Kestrel ports: HTTP/1.1 for cards, JSON-RPC and REST, and one h2c port per agent for gRPC
/// (this server version routes gRPC by port). Three agents: <see cref="ContractPayloads.MainAgent"/> (every transport, streaming, push,
/// extended card), <see cref="ContractPayloads.RequiredExtensionAgent"/> and <see cref="ContractPayloads.TenantAgent"/>.
/// Public operations are anonymous; the extended card needs <c>Bearer interop-test-token</c>. Public APIs only.
/// </summary>
public sealed class InteropServerHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private InteropServerHost(WebApplication app, string publicHost, int httpPort, IReadOnlyDictionary<string, int> grpcPorts)
    {
        _app = app;
        PublicHost = publicHost;
        HttpPort = httpPort;
        GrpcPorts = grpcPorts;
    }

    /// <summary>Host in the public URLs.</summary>
    public string PublicHost { get; }

    /// <summary>The HTTP port (cards, JSON-RPC, REST).</summary>
    public int HttpPort { get; }

    /// <summary>The gRPC port per agent name.</summary>
    public IReadOnlyDictionary<string, int> GrpcPorts { get; }

    /// <summary>Base URL of the HTTP listener, without a trailing slash.</summary>
    public string BaseUrl => $"http://{PublicHost}:{HttpPort}";

    /// <summary>Card of the main agent at the well-known root path.</summary>
    public string CardUrl => $"{BaseUrl}/.well-known/agent-card.json";

    /// <summary>Card of the agent that requires <see cref="ContractPayloads.RequiredExtension"/>.</summary>
    public string SecondAgentCardUrl => AgentCardUrl(ContractPayloads.RequiredExtensionAgent);

    /// <summary>Card of the agent whose interfaces carry <see cref="ContractPayloads.Tenant"/>.</summary>
    public string TenantCardUrl => AgentCardUrl(ContractPayloads.TenantAgent);

    /// <summary>JSON-RPC endpoint of the main agent.</summary>
    public string JsonRpcUrl => $"{BaseUrl}/agents/{ContractPayloads.MainAgent}/jsonrpc";

    /// <summary>REST base of the main agent.</summary>
    public string RestUrl => $"{BaseUrl}/agents/{ContractPayloads.MainAgent}/rest";

    /// <summary>gRPC address (<c>host:port</c>) of the main agent.</summary>
    public string GrpcUrl => $"{PublicHost}:{GrpcPorts[ContractPayloads.MainAgent]}";

    private string AgentCardUrl(string agent) => $"{BaseUrl}/agents/{agent}/.well-known/agent-card.json";

    /// <summary>Starts the host on free loopback ports and returns when it listens.</summary>
    public static async Task<InteropServerHost> StartAsync(InteropHostOptions? options = null, CancellationToken ct = default)
    {
        options ??= new InteropHostOptions();
        var ports = FreePorts(4);
        var httpPort = ports[0];
        var grpcPorts = new Dictionary<string, int>
        {
            [ContractPayloads.MainAgent] = ports[1],
            [ContractPayloads.RequiredExtensionAgent] = ports[2],
            [ContractPayloads.TenantAgent] = ports[3],
        };

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication(BearerTokenHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, BearerTokenHandler>(BearerTokenHandler.SchemeName, null);
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<SlowTaskGate>();
        builder.Services
            .AddA2AServer(o =>
            {
                o.PublicBaseUrl = $"http://{options.PublicHost}:{httpPort}";
                o.DefaultAgent = ContractPayloads.MainAgent;
                foreach (var (agent, port) in grpcPorts) o.GrpcPorts[agent] = port;
                // All interfaces of this one agent carry the tenant; the main agent has none (see ContractPayloads.TenantAgent).
                o.InterfaceTenants[ContractPayloads.TenantAgent] = ContractPayloads.Tenant;
                o.Push.AllowPrivateNetworks = true; // PUSH-02 delivers to a harness receiver on loopback or the Docker host
            })
            .AddA2AGrpc()
            .AddAgent<InteropAgent>(ContractPayloads.MainAgent, _ => InteropCards.MainCard(extended: false), _ => InteropCards.MainCard(extended: true))
            .AddAgent<RequiredExtensionAgent>(ContractPayloads.RequiredExtensionAgent, _ => InteropCards.RequiredExtension())
            .AddAgent<InteropAgent>(ContractPayloads.TenantAgent, _ => InteropCards.Tenant());
        builder.ConfigureA2AKestrel();

        var app = builder.Build();
        app.UseAuthentication();
        app.MapA2A();
        app.MapA2AGrpc();
        await app.StartAsync(ct);
        return new InteropServerHost(app, options.PublicHost, httpPort, grpcPorts);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>Reserves <paramref name="count"/> distinct free ports, then releases them all.</summary>
    private static int[] FreePorts(int count)
    {
        var listeners = Enumerable.Range(0, count).Select(_ => new TcpListener(IPAddress.Loopback, 0)).ToArray();
        try
        {
            foreach (var l in listeners) l.Start();
            return listeners.Select(l => ((IPEndPoint)l.LocalEndpoint).Port).ToArray();
        }
        finally
        {
            foreach (var l in listeners) l.Stop();
        }
    }
}

/// <summary>The three cards of the interop server.</summary>
internal static class InteropCards
{
    private static AgentCard Base(string name, string description) => new()
    {
        Name = name,
        Description = description,
        Version = "1.0.0",
        Provider = new AgentProvider { Organization = "Ambinder.A2A interop", Url = "https://example.invalid/interop" },
        Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = true },
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain", "application/json"],
        Skills = [new AgentSkill { Id = "interop", Name = "Interop", Description = "Acts on the first word of the message.", Tags = ["interop"] }],
    };

    public static AgentCard MainCard(bool extended)
    {
        var card = Base("Interop Agent", "Interop test agent (.NET).");
        card = card with { Capabilities = card.Capabilities with { ExtendedAgentCard = true } };
        if (!extended) return card;
        return card with
        {
            Description = "Interop test agent (.NET), extended card.",
            SecuritySchemes = new Dictionary<string, SecurityScheme>
            {
                ["bearer"] = new() { HttpAuthSecurityScheme = new HttpAuthSecurityScheme { Scheme = "Bearer" } },
            },
            SecurityRequirements = [new SecurityRequirement { Schemes = new Dictionary<string, StringList> { ["bearer"] = new() } }],
        };
    }

    public static AgentCard RequiredExtension()
    {
        var card = Base("Interop Required Extension Agent", "Requires the urn:a2a:interop:required extension.");
        return card with
        {
            Capabilities = card.Capabilities with
            {
                Extensions = [new AgentExtension { Uri = ContractPayloads.RequiredExtension, Required = true, Description = "Interop required extension." }],
            },
        };
    }

    public static AgentCard Tenant() => Base("Interop Tenant Agent", "All interfaces carry the tenant interop-tenant.");
}

/// <summary>
/// <c>Authorization: Bearer interop-test-token</c> signs in one fixed app caller (with the <c>tid</c> and <c>azp</c> claims the owner
/// rules read). No header: anonymous. Any other token: authentication fails and the caller stays anonymous (public operations still succeed; the extended card answers 401).
/// </summary>
internal sealed class BearerTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "InteropBearer";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
        if (header != $"Bearer {ContractPayloads.BearerToken}") return Task.FromResult(AuthenticateResult.Fail("Unknown token."));
        var identity = new ClaimsIdentity(
            [new Claim("sub", "interop-app"), new Claim("azp", "interop-app"), new Claim("tid", "interop-tenant-id")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
