using System.Net;
using System.Text;
using A2A.Client;
using A2A.Client.Auth;
using A2A.Client.Transports.Sse;
using A2A.Protocol;
using Azure.Core;

namespace A2A.Integration.Tests.Client;

public sealed class CredentialProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            return await respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static AgentCard OAuthCard(string tokenUrl) => new()
    {
        Name = "oauth-agent",
        SecuritySchemes = new Dictionary<string, SecurityScheme>
        {
            ["entra"] = new()
            {
                Oauth2SecurityScheme = new OAuth2SecurityScheme
                {
                    Flows = new OAuthFlows { ClientCredentials = new ClientCredentialsOAuthFlow { TokenUrl = tokenUrl } },
                },
            },
        },
    };

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task SseReader_handles_multiline_and_crlf()
    {
        const string raw =
            ": comment\r\n" +
            "event: message\r\n" +
            "id: 1\r\n" +
            "data: one\r\n" +
            "\r\n" +
            "data:two-a\n" +
            "data: two-b\n" +
            "\n" +
            "data\n" +
            "\n" +
            "\n" +
            "data: last-without-blank-line";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));

        var events = new List<string>();
        await foreach (var e in SseReader.ReadAsync(stream, Ct)) events.Add(e);

        Assert.Equal(["one", "two-a\ntwo-b", "", "last-without-blank-line"], events);
    }

    [Fact]
    public async Task ClientCredentialsProvider_reads_token_url_from_card_and_caches()
    {
        var time = new FakeTime(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var issued = 0;
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK,
            $$"""{"access_token":"tok-{{++issued}}","token_type":"Bearer","expires_in":3600}""")));
        using var provider = new ClientCredentialsProvider("cid", "secret", "api://a2a-demo/.default",
            httpClient: new HttpClient(handler), timeProvider: time);
        var card = OAuthCard("http://idp.test/tenant/oauth2/v2.0/token");

        var first = await provider.GetHeadersAsync(card, Ct);
        var second = await provider.GetHeadersAsync(card, Ct);

        Assert.Equal("Bearer tok-1", first["Authorization"]);
        Assert.Equal("Bearer tok-1", second["Authorization"]);
        var call = Assert.Single(handler.Calls);
        Assert.Equal("http://idp.test/tenant/oauth2/v2.0/token", call.Request.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, call.Request.Method);
        Assert.Contains("grant_type=client_credentials", call.Body);
        Assert.Contains("client_id=cid", call.Body);
        Assert.Contains("client_secret=secret", call.Body);
        Assert.Contains("scope=api%3A%2F%2Fa2a-demo%2F.default", call.Body);

        // Within 60 s of expiry the token is refreshed.
        time.Now += TimeSpan.FromSeconds(3600 - 59);
        var third = await provider.GetHeadersAsync(card, Ct);
        Assert.Equal("Bearer tok-2", third["Authorization"]);
        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task ClientCredentialsProvider_override_wins_over_card()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, """{"access_token":"t","expires_in":60}""")));
        using var provider = new ClientCredentialsProvider("c", "s", "scope", new Uri("http://override.test/token"), new HttpClient(handler));

        await provider.GetHeadersAsync(new AgentCard(), Ct);

        Assert.Equal("http://override.test/token", Assert.Single(handler.Calls).Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task ClientCredentialsProvider_token_failure_is_auth_exception_with_oauth_error()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.BadRequest,
            """{"error":"invalid_client","error_description":"bad secret"}""")));
        using var provider = new ClientCredentialsProvider("c", "s", "scope", httpClient: new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<A2AAuthException>(async () => await provider.GetHeadersAsync(OAuthCard("http://idp.test/t"), Ct));

        Assert.Equal(401, ex.HttpStatus);
        Assert.Contains("invalid_client", ex.Message);
        Assert.Contains("bad secret", ex.Message);
    }

    [Fact]
    public async Task ClientCredentialsProvider_is_safe_under_concurrency()
    {
        var handler = new StubHandler(async _ =>
        {
            await Task.Delay(50);
            return Json(HttpStatusCode.OK, """{"access_token":"t","expires_in":3600}""");
        });
        using var provider = new ClientCredentialsProvider("c", "s", "scope", httpClient: new HttpClient(handler));
        var card = OAuthCard("http://idp.test/t");

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => provider.GetHeadersAsync(card, Ct).AsTask()));

        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task ApiKeyCredentialProvider_uses_card_header_name()
    {
        var card = new AgentCard
        {
            SecuritySchemes = new Dictionary<string, SecurityScheme>
            {
                ["key"] = new() { ApiKeySecurityScheme = new ApiKeySecurityScheme { Location = "header", Name = "X-API-Key" } },
            },
        };

        var headers = await new ApiKeyCredentialProvider("dev-api-key").GetHeadersAsync(card, Ct);

        Assert.Equal("dev-api-key", Assert.Single(headers).Value);
        Assert.Equal("X-API-Key", Assert.Single(headers).Key);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("cookie")]
    public async Task ApiKeyCredentialProvider_rejects_query_and_cookie(string location)
    {
        var card = new AgentCard
        {
            SecuritySchemes = new Dictionary<string, SecurityScheme>
            {
                ["key"] = new() { ApiKeySecurityScheme = new ApiKeySecurityScheme { Location = location, Name = "k" } },
            },
        };

        var ex = await Assert.ThrowsAsync<NotSupportedException>(async () => await new ApiKeyCredentialProvider("x").GetHeadersAsync(card, Ct));
        Assert.Contains(location, ex.Message);
    }

    [Fact]
    public async Task BearerTokenCredentialProvider_sends_authorization_header()
    {
        var headers = await new BearerTokenCredentialProvider("abc").GetHeadersAsync(new AgentCard(), Ct);
        Assert.Equal("Bearer abc", headers["Authorization"]);
    }

    private sealed class FakeCredential : TokenCredential
    {
        public string[]? Scopes { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Scopes = requestContext.Scopes;
            return new AccessToken("azure-token", DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    [Fact]
    public async Task AzureIdentityCredentialProvider_requests_scope_and_sends_bearer()
    {
        var credential = new FakeCredential();
        var headers = await new AzureIdentityCredentialProvider(credential, "api://a2a-demo/.default").GetHeadersAsync(new AgentCard(), Ct);

        Assert.Equal("Bearer azure-token", headers["Authorization"]);
        Assert.Equal(["api://a2a-demo/.default"], credential.Scopes!);
    }

    [Fact]
    public async Task CardResolver_appends_well_known_path_and_caches_for_five_minutes()
    {
        var time = new FakeTime(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, """{"name":"echo","version":"1.0.0"}""")));
        var resolver = new A2ACardResolver(new HttpClient(handler), time);

        var card = await resolver.GetAgentCardAsync(new Uri("http://host.test/agents/echo/"), Ct);
        await resolver.GetAgentCardAsync(new Uri("http://host.test/agents/echo"), Ct);

        Assert.Equal("echo", card.Name);
        Assert.Single(handler.Calls);
        Assert.Equal("http://host.test/agents/echo/.well-known/agent-card.json", handler.Calls[0].Request.RequestUri!.ToString());

        time.Now += TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1);
        await resolver.GetAgentCardAsync(new Uri("http://host.test/agents/echo"), Ct);
        Assert.Equal(2, handler.Calls.Count);

        await resolver.GetAgentCardAsync(new Uri("http://host.test/custom/card.json"), Ct);
        Assert.Equal("http://host.test/custom/card.json", handler.Calls[2].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task CardResolver_maps_http_failures()
    {
        var resolver = new A2ACardResolver(new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))));
        var auth = await Assert.ThrowsAsync<A2AAuthException>(async () => await resolver.GetAgentCardAsync(new Uri("http://h.test/"), Ct));
        Assert.Equal(403, auth.HttpStatus);

        resolver = new A2ACardResolver(new HttpClient(new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, "not json")))));
        var bad = await Assert.ThrowsAsync<A2AException>(async () => await resolver.GetAgentCardAsync(new Uri("http://h.test/"), Ct));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, bad.Code);
    }
}
