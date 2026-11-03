using System.Net;
using System.Net.Http.Headers;
using System.Text;
using A2A.Client;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;
using AgentServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace A2A.Integration.Tests.Flows;

[Collection(A2ATestHostCollection.Name)]
public sealed class DiscoveryAndAuthTests(A2ATestHost host)
{
    private const string AuditExtension = "https://example.com/a2a/ext/audit/v1";
    private const string TimestampExtension = "https://example.com/a2a/ext/timestamp/v1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SendMessageRequest Send(string text) => new()
    {
        Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText(text)] },
    };

    private async Task<SendMessageResponse> SendAsync(string agent, string binding, TestAuth auth, IReadOnlyList<string>? extensions = null)
    {
        await using var client = await host.CreateClientAsync(agent, binding, auth, extensions, Ct);
        return await client.SendMessageAsync(Send("hello"), Ct);
    }

    private async Task<int> AuthFailureAsync(string agent, string binding, TestAuth auth, IReadOnlyList<string>? extensions = null)
        => (await Assert.ThrowsAsync<A2AAuthException>(() => SendAsync(agent, binding, auth, extensions))).HttpStatus;

    [Fact]
    public async Task Root_card_is_report_agent()
    {
        var card = await host.Cards.GetAgentCardAsync(new Uri(A2ATestHost.BaseUrl), Ct);
        Assert.Equal("report", card.Name);
        Assert.Contains(card.SupportedInterfaces, i => i.Url == $"{A2ATestHost.BaseUrl}/agents/report/jsonrpc");
    }

    [Fact]
    public async Task Card_security_matches_config()
    {
        var card = await host.GetCardAsync("report", Ct);
        var schemes = card.SecuritySchemes!;

        var flow = schemes["entra"].Oauth2SecurityScheme!.Flows.ClientCredentials!;
        Assert.Equal(A2ATestHost.TokenUrl, flow.TokenUrl);
        Assert.Equal("Invoke A2A demo agents", flow.Scopes[A2ATestHost.Scope]);
        Assert.Equal(A2ATestHost.OpenIdConfigurationUrl, schemes["entraOidc"].OpenIdConnectSecurityScheme!.OpenIdConnectUrl);
        var apiKey = schemes["apiKey"].ApiKeySecurityScheme!;
        Assert.Equal(("header", "X-API-Key"), (apiKey.Location, apiKey.Name));

        // entra OR apiKey = two alternative requirements.
        Assert.Equal(["entra", "apiKey"], card.SecurityRequirements.Select(r => r.Schemes.Keys.Single()));
        Assert.Equal([A2ATestHost.Scope], card.SecurityRequirements[0].Schemes["entra"].List);

        var secure = await host.GetCardAsync("secure", Ct);
        Assert.Equal(["entra"], secure.SecurityRequirements.Select(r => r.Schemes.Keys.Single()));
        Assert.False(secure.SecuritySchemes!.ContainsKey("apiKey")); // bearer only: no API key scheme advertised
        var echo = await host.GetCardAsync("echo", Ct);
        Assert.Empty(echo.SecurityRequirements);
        Assert.Null(echo.SecuritySchemes);
    }

    [Theory]
    [InlineData("report", false, false)]
    [InlineData("booking", false, false)]
    [InlineData("secure", false, false)]
    [InlineData("report", true, true)]
    [InlineData("booking", true, true)]
    [InlineData("secure", true, false)]
    public void Api_key_scheme_only_when_keys_are_configured_and_accepted(string agent, bool keysConfigured, bool expected)
    {
        var auth = new AgentServer.Auth.AuthOptions { Audience = "api://a", TokenUrl = "https://idp/t", OpenIdConfigurationUrl = "https://idp/o" };
        if (keysConfigured) auth.ApiKeys["dev"] = "k";
        var card = AgentServer.Cards.AgentCards.Build(agent, new A2A.Server.Hosting.A2AServerOptions { PublicBaseUrl = "http://h" }, auth, extended: false);

        Assert.Equal(expected, card.SecuritySchemes!.ContainsKey("apiKey"));
        Assert.Equal(expected, card.SecurityRequirements.Any(r => r.Schemes.ContainsKey("apiKey")));
        // Every requirement names a declared scheme.
        Assert.All(card.SecurityRequirements.SelectMany(r => r.Schemes.Keys), k => Assert.True(card.SecuritySchemes.ContainsKey(k)));
    }

    [Theory]
    [InlineData("echo", true, false, false, "text/plain,application/json,*/*", TimestampExtension, false)]
    [InlineData("report", true, true, true, "text/plain", null, false)]
    [InlineData("booking", true, true, false, "text/plain", null, false)]
    [InlineData("secure", true, true, true, "text/plain", AuditExtension, true)]
    public async Task Cards_match_agent_table(string agent, bool streaming, bool push, bool extended, string inputModes,
        string? extension, bool required)
    {
        var card = await host.GetCardAsync(agent, Ct);
        Assert.Equal(agent, card.Name);
        Assert.Equal((streaming, push, extended),
            (card.Capabilities.Streaming == true, card.Capabilities.PushNotifications == true, card.Capabilities.ExtendedAgentCard == true));
        Assert.Equal(inputModes.Split(','), card.DefaultInputModes);
        Assert.Equal(["JSONRPC", "HTTP+JSON", "GRPC"], card.SupportedInterfaces.Select(i => i.ProtocolBinding));
        Assert.Equal($"127.0.0.1:{A2ATestHost.PortBase + A2ATestHost.GrpcOffsets[agent]}", card.SupportedInterfaces[2].Url);
        if (extension is null)
        {
            Assert.Empty(card.Capabilities.Extensions);
        }
        else
        {
            var ext = Assert.Single(card.Capabilities.Extensions);
            Assert.Equal((extension, required), (ext.Uri, ext.Required));
        }
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Echo_allows_anonymous(string binding)
    {
        var response = await SendAsync("echo", binding, TestAuth.None);
        Assert.True(response.Task is not null || response.Message is not null);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Report_without_credentials_is_401(string binding)
        => Assert.Equal(401, await AuthFailureAsync("report", binding, TestAuth.None));

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Report_with_noroles_token_is_403(string binding)
        => Assert.Equal(403, await AuthFailureAsync("report", binding, TestAuth.NoRoles));

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Report_with_client_token_succeeds(string binding)
        => Assert.NotNull((await SendAsync("report", binding, TestAuth.Client)).Task);

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Report_with_api_key_succeeds(string binding)
        => Assert.NotNull((await SendAsync("report", binding, TestAuth.ApiKey)).Task);

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Secure_rejects_api_key_with_403(string binding)
        => Assert.Equal(403, await AuthFailureAsync("secure", binding, TestAuth.ApiKey, [AuditExtension]));

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Secure_accepts_client_token(string binding)
        => Assert.NotNull((await SendAsync("secure", binding, TestAuth.Client, [AuditExtension])).Task);

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Extended_card_requires_auth_and_adds_skills(string binding)
    {
        await using (var anonymous = await host.CreateClientAsync("report", binding, TestAuth.None, ct: Ct))
        {
            var ex = await Assert.ThrowsAsync<A2AAuthException>(() => anonymous.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest(), Ct));
            Assert.Equal(401, ex.HttpStatus);
        }

        await using var client = await host.CreateClientAsync("report", binding, TestAuth.Client, ct: Ct);
        var extended = await client.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest(), Ct);
        Assert.Contains(extended.Skills, s => s.Id == "report-archive");
        Assert.DoesNotContain((await host.GetCardAsync("report", Ct)).Skills, s => s.Id == "report-archive");
        Assert.Equal(3, extended.SupportedInterfaces.Count);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Echo_extended_card_not_offered_is_unsupported_operation(string binding)
    {
        // Spec 3.3.4: capabilities.extendedAgentCard false → UnsupportedOperationError.
        await using var client = await host.CreateClientAsync("echo", binding, TestAuth.Client, ct: Ct);
        var ex = await Assert.ThrowsAsync<A2AException>(() => client.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest(), Ct));
        Assert.Equal(A2AErrorCode.UnsupportedOperation, ex.Code);
        Assert.Equal(-32004, (int)ex.Code);
    }

    [Fact]
    public async Task Wrong_api_key_is_401()
    {
        using var request = JsonRpcSend($"{A2ATestHost.BaseUrl}/agents/report/jsonrpc");
        request.Headers.Add("X-API-Key", "not-the-key");
        using var response = await host.Http.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Mock_entra_down_gives_401_not_500()
    {
        // A second AgentServer whose authority is a port with nothing listening, so the first token validation cannot
        // fetch the OpenID metadata. The token itself comes from the running MockEntra.
        var portBase = A2ATestHost.PortBase + 2000;
        var token = await host.GetTokenAsync(TestAuth.Client, Ct);
        var logs = new CapturingLoggerProvider();
        await using var down = await A2ATestHost.StartAgentServerAsync(portBase, $"http://127.0.0.1:{portBase + 100}",
            b => b.Logging.AddProvider(logs));

        using var request = JsonRpcSend($"{A2ATestHost.AgentServerBaseUrl(portBase)}/agents/report/jsonrpc");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await host.Http.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Still 401 on the next call (no cached failure turning into 500).
        using var again = JsonRpcSend($"{A2ATestHost.AgentServerBaseUrl(portBase)}/agents/report/jsonrpc");
        again.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var second = await host.Http.SendAsync(again, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);

        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("OpenID metadata", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("AzureOpenAI")]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    [InlineData("Ollama")]
    public void Unknown_llm_provider_fails_startup_with_phase2_message(string provider)
    {
        var ex = Assert.Throws<NotSupportedException>(() => AgentServerApp.Build(["--environment", "Development"], b =>
        {
            b.Configuration.AddInMemoryCollection(A2ATestHost.AgentServerConfig(A2ATestHost.PortBase + 3000, A2ATestHost.MockEntraBaseUrl));
            b.Configuration["Llm:Provider"] = provider;
        }));
        Assert.Equal($"LLM provider '{provider}' is planned for Phase 2. Use Llm:Provider=Fake.", ex.Message);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_respond(string path)
    {
        using var response = await host.Http.GetAsync(A2ATestHost.BaseUrl + path, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpRequestMessage JsonRpcSend(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"messageId":"m1","role":"ROLE_USER","parts":[{"text":"hi"}]}}}""",
                Encoding.UTF8, "application/a2a+json"),
        };
        request.Headers.Add("A2A-Version", "1.0");
        return request;
    }
}

/// <summary>Collects log entries in memory.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => owner.Entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
