using System.Collections.Concurrent;
using System.Text.Json;
using Ambinder.A2A.Client;
using Ambinder.A2A.Client.Auth;
using Ambinder.A2A.Protocol;
using AgentServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MockEntra;
using SampleTelemetry;
using WebhookReceiver;

namespace Ambinder.A2A.Integration.Tests.Fixtures;

/// <summary>How a test client authenticates.</summary>
public enum TestAuth
{
    None,
    ApiKey,
    /// <summary>Client credentials as <c>a2a-client</c> (role A2A.Invoke).</summary>
    Client,
    /// <summary>Client credentials as <c>a2a-approver</c> (roles A2A.Invoke, Reports.Approve).</summary>
    Approver,
    /// <summary>Client credentials as <c>a2a-noroles</c> (no roles).</summary>
    NoRoles,
}

/// <summary>
/// The real samples on real Kestrel: MockEntra (base + 100), WebhookReceiver (base + 300) and AgentServer (HTTP base + 200, gRPC base + 201..204), started
/// through their <c>Build</c> methods. Base = env <c>A2A_TEST_PORT_BASE</c>, default 15000. One instance per test run
/// (collection fixture <see cref="A2ATestHostCollection"/>). Fast timings: agent step delay 20 ms, push backoff 10 ms.
/// </summary>
public sealed class A2ATestHost : IAsyncLifetime
{
    public const string Tenant = "a2a00000-0000-0000-0000-000000000001";
    public const string Scope = "api://a2a-demo/.default";
    public const string ApiKey = "dev-api-key";
    public const int DefaultPortBase = 15000;
    public static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(20);
    public static readonly TimeSpan PushBackoff = TimeSpan.FromMilliseconds(10);

    /// <summary>Agent names and their gRPC port offsets (base + offset).</summary>
    public static readonly IReadOnlyDictionary<string, int> GrpcOffsets = new Dictionary<string, int>
    {
        ["echo"] = 201, ["report"] = 202, ["booking"] = 203, ["secure"] = 204,
    };

    private static readonly IReadOnlyDictionary<TestAuth, (string Id, string Secret)> Clients = new Dictionary<TestAuth, (string, string)>
    {
        [TestAuth.Client] = ("a2a-client", "dev-client-secret"),
        [TestAuth.Approver] = ("a2a-approver", "dev-approver-secret"),
        [TestAuth.NoRoles] = ("a2a-noroles", "dev-noroles-secret"),
    };

    public static int PortBase { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("A2A_TEST_PORT_BASE"), out var b) ? b : DefaultPortBase;

    public static string MockEntraBaseUrl => $"http://127.0.0.1:{PortBase + 100}";
    public static string BaseUrl => AgentServerBaseUrl(PortBase);
    /// <summary>The WebhookReceiver (trusts the test AgentServer; its public URL is this base URL).</summary>
    public static string ReceiverBaseUrl => $"http://127.0.0.1:{PortBase + 300}";
    public static string Authority => AuthorityFor(MockEntraBaseUrl);
    public static string TokenUrl => $"{MockEntraBaseUrl}/{Tenant}/oauth2/v2.0/token";
    public static string OpenIdConfigurationUrl => $"{Authority}/.well-known/openid-configuration";

    private readonly ConcurrentBag<IDisposable> _disposables = [];
    private WebApplication? _mockEntra;
    private WebApplication? _agentServer;
    private WebApplication? _receiver;

    public WebApplication MockEntra => _mockEntra ?? throw new InvalidOperationException("Not started.");
    public WebApplication Receiver => _receiver ?? throw new InvalidOperationException("Not started.");
    public WebApplication AgentServer => _agentServer ?? throw new InvalidOperationException("Not started.");

    /// <summary>Shared HTTP client (no base address, no auth headers).</summary>
    public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(30) };

    public A2ACardResolver Cards { get; }

    public A2ATestHost() => Cards = new A2ACardResolver(Http);

    public async ValueTask InitializeAsync()
    {
        _mockEntra = MockEntraApp.Build(["--environment", "Development"], builder =>
        {
            builder.Logging.ClearProviders();
            builder.Configuration[SampleTelemetryExtensions.EndpointKey] = ""; // a developer shell must not turn telemetry on in tests
            builder.WebHost.UseUrls(MockEntraBaseUrl);
            builder.Configuration["MockEntra:IssuerBaseUrl"] = MockEntraBaseUrl;
        });
        await _mockEntra.StartAsync();

        _receiver = await StartReceiverAsync(PortBase + 300, [AgentServerBaseUrl(PortBase)]);

        _agentServer = await StartAgentServerAsync(PortBase, MockEntraBaseUrl);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables) d.Dispose();
        if (_agentServer is not null) await _agentServer.DisposeAsync();
        if (_receiver is not null) await _receiver.DisposeAsync();
        if (_mockEntra is not null) await _mockEntra.DisposeAsync();
        Http.Dispose();
    }

    /// <summary>Starts a WebhookReceiver on <paramref name="port"/> (public URL = its own base URL) trusting <paramref name="trustedServers"/>.</summary>
    public static async Task<WebApplication> StartReceiverAsync(int port, IReadOnlyList<string> trustedServers)
    {
        var url = $"http://127.0.0.1:{port}";
        var app = WebhookReceiverApp.Build(["--environment", "Development"], builder =>
        {
            builder.Logging.ClearProviders();
            builder.Configuration[SampleTelemetryExtensions.EndpointKey] = ""; // a developer shell must not turn telemetry on in tests
            builder.WebHost.UseUrls(url);
            builder.Configuration["Receiver:PublicUrl"] = url;
            for (var i = 0; i < trustedServers.Count; i++) builder.Configuration[$"Receiver:TrustedServers:{i}"] = trustedServers[i];
        });
        await app.StartAsync();
        return app;
    }

    public static string AgentServerBaseUrl(int portBase) => $"http://127.0.0.1:{portBase + 200}";

    public static string AuthorityFor(string mockEntraBaseUrl) => $"{mockEntraBaseUrl}/{Tenant}/v2.0";

    /// <summary>The AgentServer configuration overrides for a port base and identity provider.</summary>
    public static Dictionary<string, string?> AgentServerConfig(int portBase, string mockEntraBaseUrl)
    {
        var authority = AuthorityFor(mockEntraBaseUrl);
        var config = new Dictionary<string, string?>
        {
            ["A2A:PublicBaseUrl"] = AgentServerBaseUrl(portBase),
            ["A2A:Push:InitialBackoff"] = PushBackoff.ToString(),
            ["A2A:Push:AllowPrivateNetworks"] = "true",
            ["Auth:Authority"] = authority,
            ["Auth:TokenUrl"] = $"{mockEntraBaseUrl}/{Tenant}/oauth2/v2.0/token",
            ["Auth:OpenIdConfigurationUrl"] = $"{authority}/.well-known/openid-configuration",
            ["Auth:RequireHttpsMetadata"] = "false",
            ["FakeAgents:StepDelay"] = StepDelay.ToString(),
            ["Llm:Provider"] = "Fake",
        };
        foreach (var (agent, offset) in GrpcOffsets) config[$"A2A:GrpcPorts:{agent}"] = (portBase + offset).ToString();
        return config;
    }

    /// <summary>Starts an AgentServer (Development) on <paramref name="portBase"/> trusting <paramref name="mockEntraBaseUrl"/>.</summary>
    public static async Task<WebApplication> StartAgentServerAsync(int portBase, string mockEntraBaseUrl,
        Action<WebApplicationBuilder>? configure = null)
    {
        var app = AgentServerApp.Build(["--environment", "Development"], builder =>
        {
            builder.Logging.ClearProviders();
            builder.Configuration[SampleTelemetryExtensions.EndpointKey] = ""; // a developer shell must not turn telemetry on in tests
            builder.Configuration.AddInMemoryCollection(AgentServerConfig(portBase, mockEntraBaseUrl));
            configure?.Invoke(builder);
        });
        await app.StartAsync();
        return app;
    }

    /// <summary>The public card of an agent, through the card resolver.</summary>
    public Task<AgentCard> GetCardAsync(string agent, CancellationToken ct = default)
        => Cards.GetAgentCardAsync(new Uri($"{BaseUrl}/agents/{agent}"), ct);

    /// <summary>A client for <paramref name="agent"/> over <paramref name="binding"/>, built from its card with the real providers.</summary>
    public async Task<IA2AClient> CreateClientAsync(string agent, string binding, TestAuth auth = TestAuth.None,
        IReadOnlyList<string>? extensions = null, CancellationToken ct = default)
    {
        var card = await GetCardAsync(agent, ct);
        return A2AClientFactory.Create(card, new A2AClientOptions
        {
            HttpClient = Http,
            Credentials = Credentials(auth, card),
            PreferredBinding = binding,
            Extensions = extensions ?? [],
        });
    }

    /// <summary>
    /// The credential provider for <paramref name="auth"/>. Client credentials use the card's token URL (its loopback MockEntra
    /// host is in AllowedTokenHosts); for a card without an oauth2 scheme (echo) the MockEntra token URL is passed as override.
    /// </summary>
    public IA2ACredentialProvider? Credentials(TestAuth auth, AgentCard card)
    {
        switch (auth)
        {
            case TestAuth.None:
                return null;
            case TestAuth.ApiKey:
                // A card that does not advertise an API key scheme (secure) still gets the header, to prove the server refuses it.
                return card.SecuritySchemes?.Values.Any(s => s.ApiKeySecurityScheme is not null) == true
                    ? new ApiKeyCredentialProvider(ApiKey)
                    : new RawHeaderCredentials("X-API-Key", ApiKey);
            default:
                var (id, secret) = Clients[auth];
                var hasFlow = card.SecuritySchemes?.Values.Any(s => s.Oauth2SecurityScheme?.Flows.ClientCredentials is not null) == true;
                // Cards with a flow name the loopback MockEntra; that host is allowed explicitly. Echo has no flow: override.
                var provider = new ClientCredentialsProvider(id, secret, Scope, hasFlow ? null : new Uri(TokenUrl), Http,
                    allowedTokenHosts: [new Uri(TokenUrl).Host]);
                _disposables.Add(provider);
                return provider;
        }
    }

    /// <summary>A raw access token from MockEntra for a client-credentials <paramref name="auth"/>.</summary>
    public async Task<string> GetTokenAsync(TestAuth auth, CancellationToken ct = default)
    {
        var (id, secret) = Clients[auth];
        using var response = await Http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = id, ["client_secret"] = secret, ["scope"] = Scope,
        }), ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }
}

/// <summary>Sends one fixed header, whatever the card declares.</summary>
public sealed class RawHeaderCredentials(string name, string value) : IA2ACredentialProvider
{
    public ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct)
        => new(new Dictionary<string, string> { [name] = value });
}

[CollectionDefinition(Name)]
public sealed class A2ATestHostCollection : ICollectionFixture<A2ATestHost>
{
    public const string Name = "A2ATestHost";
}
