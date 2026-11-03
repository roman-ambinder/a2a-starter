using System.Text;
using SampleTelemetry;

namespace MockEntra;

public static class MockEntraApp
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException("MockEntra runs only in Development");

        configure?.Invoke(builder);
        builder.AddSampleTelemetry("mockentra");

        var options = builder.Configuration.GetSection("MockEntra").Get<MockEntraOptions>() ?? new MockEntraOptions();
        var issuer = new TokenIssuer(options);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(issuer);

        var app = builder.Build();
        var baseUrl = options.IssuerBaseUrl.TrimEnd('/');

        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

        app.MapGet("/{tenant}/v2.0/.well-known/openid-configuration", (string tenant) =>
        {
            if (tenant != options.TenantId) return TenantNotFound(tenant);
            var t = $"{baseUrl}/{tenant}";
            return Results.Json(new Dictionary<string, object>
            {
                ["issuer"] = $"{t}/v2.0",
                ["authorization_endpoint"] = $"{t}/oauth2/v2.0/authorize",
                ["token_endpoint"] = $"{t}/oauth2/v2.0/token",
                ["jwks_uri"] = $"{t}/discovery/v2.0/keys",
                ["response_types_supported"] = new[] { "id_token", "token" },
                ["subject_types_supported"] = new[] { "pairwise" },
                ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
                ["token_endpoint_auth_methods_supported"] = new[] { "client_secret_post", "client_secret_basic" },
            });
        });

        app.MapGet("/{tenant}/discovery/v2.0/keys", (string tenant) =>
            tenant != options.TenantId ? TenantNotFound(tenant) : Results.Json(issuer.Jwks()));

        app.MapGet("/{tenant}/oauth2/v2.0/authorize", () =>
            Results.Json(new { error = "not_implemented", error_description = "Interactive flows are not supported by MockEntra." },
                statusCode: StatusCodes.Status501NotImplemented));

        app.MapPost("/{tenant}/oauth2/v2.0/token", async (string tenant, HttpRequest request) =>
        {
            if (tenant != options.TenantId) return TenantNotFound(tenant);
            if (!request.HasFormContentType)
                return Error(400, "invalid_request", "AADSTS900144: The request body must contain form parameters.");
            var form = await request.ReadFormAsync();

            if (form["grant_type"] != "client_credentials")
                return Error(400, "unsupported_grant_type",
                    "AADSTS70003: The app requested an unsupported grant type. Only client_credentials is supported.");

            var clientId = form["client_id"].ToString();
            var secret = form["client_secret"].ToString();
            if (request.Headers.Authorization.ToString() is { Length: > 0 } auth &&
                auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth[6..]));
                    var i = decoded.IndexOf(':');
                    if (i > 0)
                    {
                        clientId = Uri.UnescapeDataString(decoded[..i]);
                        secret = Uri.UnescapeDataString(decoded[(i + 1)..]);
                    }
                }
                catch (FormatException)
                {
                    return Error(401, "invalid_client", "AADSTS7000216: Malformed Basic authorization header.");
                }
            }

            if (string.IsNullOrEmpty(clientId))
                return Error(400, "invalid_request", "AADSTS900144: The request body must contain the parameter 'client_id'.");
            if (!issuer.IsKnownClient(clientId))
                return Error(401, "invalid_client", $"AADSTS700016: Application with identifier '{clientId}' was not found in the directory.");
            var client = issuer.Authenticate(clientId, secret);
            if (client is null)
                return Error(401, "invalid_client", "AADSTS7000215: Invalid client secret provided. Ensure the secret being sent in the request is the client secret value.");

            var audience = issuer.AudienceForScope(form["scope"]);
            if (audience is null)
                return Error(400, "invalid_scope",
                    $"AADSTS70011: The provided request must include a 'scope' input parameter. The provided value for the input parameter 'scope' is not valid. Expected '{options.Audience}/.default'.");

            var (token, expiresIn) = issuer.Issue(client, audience);
            return Results.Json(new Dictionary<string, object>
            {
                ["token_type"] = "Bearer",
                ["expires_in"] = expiresIn,
                ["access_token"] = token,
            });
        });

        return app;
    }

    private static IResult Error(int status, string error, string description) =>
        Results.Json(new { error, error_description = description }, statusCode: status);

    private static IResult TenantNotFound(string tenant) =>
        Results.Json(new { error = "invalid_request", error_description = $"AADSTS90002: Tenant '{tenant}' not found." },
            statusCode: StatusCodes.Status404NotFound);
}
