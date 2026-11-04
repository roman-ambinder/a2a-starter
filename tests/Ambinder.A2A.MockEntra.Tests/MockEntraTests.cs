using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using MockEntra;
using SampleTelemetry;

namespace Ambinder.A2A.MockEntra.Tests;

public sealed class MockEntraFixture : IAsyncLifetime
{
    public const string Tenant = "a2a00000-0000-0000-0000-000000000001";
    public const string Scope = "api://a2a-demo/.default";

    private WebApplication? _app;
    public string Base { get; private set; } = "";
    public HttpClient Http { get; } = new();

    public async ValueTask InitializeAsync()
    {
        var offset = int.TryParse(Environment.GetEnvironmentVariable("A2A_TEST_PORT_BASE"), out var b) ? b : 15000;
        // base + 150, not + 100: the integration tests' A2ATestHost runs MockEntra on base + 100, and test projects run in parallel.
        Base = $"http://127.0.0.1:{offset + 150}";
        _app = MockEntraApp.Build(["--environment", "Development"], builder =>
        {
            builder.Configuration[SampleTelemetryExtensions.EndpointKey] = ""; // a developer shell must not turn telemetry on in tests
            builder.WebHost.UseUrls(Base);
            builder.Configuration["MockEntra:IssuerBaseUrl"] = Base;
        });
        await _app.StartAsync();
        Http.BaseAddress = new Uri(Base);
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        if (_app is not null) await _app.DisposeAsync();
    }

    public Task<HttpResponseMessage> PostToken(string client, string secret, string scope = Scope) =>
        Http.PostAsync($"/{Tenant}/oauth2/v2.0/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = client,
            ["client_secret"] = secret,
            ["scope"] = scope,
        }));

    public async Task<string> GetToken(string client, string secret)
    {
        var res = await PostToken(client, secret);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }
}

public class MockEntraTests(MockEntraFixture fx) : IClassFixture<MockEntraFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Discovery_document_matches_entra_shape()
    {
        var res = await fx.Http.GetAsync($"/{MockEntraFixture.Tenant}/v2.0/.well-known/openid-configuration", Ct);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        var r = doc.RootElement;
        var t = $"{fx.Base}/{MockEntraFixture.Tenant}";
        Assert.Equal($"{t}/v2.0", r.GetProperty("issuer").GetString());
        Assert.Equal($"{t}/oauth2/v2.0/token", r.GetProperty("token_endpoint").GetString());
        Assert.Equal($"{t}/discovery/v2.0/keys", r.GetProperty("jwks_uri").GetString());
        Assert.Equal($"{t}/oauth2/v2.0/authorize", r.GetProperty("authorization_endpoint").GetString());
        Assert.Equal(["RS256"], r.GetProperty("id_token_signing_alg_values_supported").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["client_secret_post", "client_secret_basic"],
            r.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(e => e.GetString()));

        var keys = await fx.Http.GetAsync($"/{MockEntraFixture.Tenant}/discovery/v2.0/keys", Ct);
        using var jwks = JsonDocument.Parse(await keys.Content.ReadAsStringAsync(Ct));
        var key = jwks.RootElement.GetProperty("keys")[0];
        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.False(string.IsNullOrEmpty(key.GetProperty("kid").GetString()));

        var authz = await fx.Http.GetAsync($"/{MockEntraFixture.Tenant}/oauth2/v2.0/authorize", Ct);
        Assert.Equal(HttpStatusCode.NotImplemented, authz.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fx.Http.GetAsync("/health/live", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fx.Http.GetAsync("/health/ready", Ct)).StatusCode);
    }

    [Fact]
    public async Task Client_credentials_token_has_entra_claims()
    {
        var res = await fx.PostToken("a2a-approver", "dev-approver-secret");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        Assert.Equal("Bearer", body.RootElement.GetProperty("token_type").GetString());
        Assert.Equal(3600, body.RootElement.GetProperty("expires_in").GetInt32());

        var jwt = new JsonWebToken(body.RootElement.GetProperty("access_token").GetString()!);
        Assert.Equal($"{fx.Base}/{MockEntraFixture.Tenant}/v2.0", jwt.Issuer);
        Assert.Contains("api://a2a-demo", jwt.Audiences);
        Assert.Equal(MockEntraFixture.Tenant, jwt.GetClaim("tid").Value);
        Assert.Equal("a2a-approver", jwt.GetClaim("azp").Value);
        Assert.Equal("a2a-approver", jwt.GetClaim("appid").Value);
        Assert.Equal("2.0", jwt.GetClaim("ver").Value);
        Assert.Equal(jwt.GetClaim("oid").Value, jwt.GetClaim("sub").Value);
        Assert.Contains("Reports.Approve", jwt.GetPayloadValue<string[]>("roles"));
        Assert.Contains("A2A.Invoke", jwt.GetPayloadValue<string[]>("roles"));
        Assert.True(jwt.ValidTo > jwt.ValidFrom);

        // oid stable per client, and roles omitted when empty
        var again = new JsonWebToken(await fx.GetToken("a2a-approver", "dev-approver-secret"));
        Assert.Equal(jwt.GetClaim("oid").Value, again.GetClaim("oid").Value);
        var noRoles = new JsonWebToken(await fx.GetToken("a2a-noroles", "dev-noroles-secret"));
        Assert.False(noRoles.TryGetPayloadValue<object>("roles", out _));
    }

    [Fact]
    public async Task Client_credentials_accepts_basic_auth()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/{MockEntraFixture.Tenant}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = MockEntraFixture.Scope,
            }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("a2a-client:dev-client-secret")));
        var res = await fx.Http.SendAsync(req, Ct);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Token_validates_with_jwt_bearer_against_discovery()
    {
        var token = await fx.GetToken("a2a-approver", "dev-approver-secret");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = $"{fx.Base}/{MockEntraFixture.Tenant}/v2.0";
                o.Audience = "api://a2a-demo";
                o.RequireHttpsMetadata = false;
            });
        builder.Services.AddAuthorization();
        await using var api = builder.Build();
        api.UseAuthentication();
        api.UseAuthorization();
        api.MapGet("/whoami", (ClaimsPrincipal user) => user.FindFirst("appid")?.Value ?? "none").RequireAuthorization();
        await api.StartAsync(Ct);
        var apiBase = api.Urls.First();

        using var http = new HttpClient { BaseAddress = new Uri(apiBase) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/whoami", Ct)).StatusCode);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await http.GetAsync("/whoami", Ct);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("a2a-approver", await res.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Wrong_secret_returns_invalid_client()
    {
        var res = await fx.PostToken("a2a-client", "nope");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        Assert.Equal("invalid_client", doc.RootElement.GetProperty("error").GetString());
        Assert.StartsWith("AADSTS7000215", doc.RootElement.GetProperty("error_description").GetString());
    }

    [Fact]
    public async Task Unknown_scope_returns_invalid_scope()
    {
        var res = await fx.PostToken("a2a-client", "dev-client-secret", "api://other/.default");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        Assert.Equal("invalid_scope", doc.RootElement.GetProperty("error").GetString());
        Assert.StartsWith("AADSTS70011", doc.RootElement.GetProperty("error_description").GetString());
    }

    [Fact]
    public void Refuses_to_start_outside_development()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => MockEntraApp.Build(["--environment", "Production"]));
        Assert.Equal("MockEntra runs only in Development", ex.Message);
    }
}
