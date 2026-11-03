using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace AgentServer.Auth;

/// <summary>Authentication and authorization for the agent server: Entra-shaped bearer tokens and API keys.</summary>
public static class AuthSetup
{
    /// <summary>Default scheme: forwards to <see cref="ApiKeyScheme"/> when <c>X-API-Key</c> is present, else to <see cref="BearerScheme"/>.</summary>
    public const string PolicyScheme = "A2A";
    public const string ApiKeyScheme = ApiKeyAuthenticationHandler.SchemeName;
    public const string BearerScheme = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>Authenticated and role <see cref="InvokeRole"/> (bearer or API key).</summary>
    public const string InvokePolicy = "Invoke";

    /// <summary><see cref="InvokePolicy"/> and the identity came from a bearer token (API keys are refused with 403).</summary>
    public const string InvokeBearerPolicy = "InvokeBearer";

    public const string InvokeRole = "A2A.Invoke";

    /// <summary>
    /// Registers the schemes and policies from the <c>Auth</c> section and returns the bound options.
    /// Throws <see cref="InvalidOperationException"/> when <c>Auth:Authority</c> or <c>Auth:Audience</c> is missing, or when
    /// <c>Auth:RequireHttpsMetadata</c> is true and the authority is not https.
    /// </summary>
    public static AuthOptions AddA2AAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        if (string.IsNullOrWhiteSpace(auth.Authority) || string.IsNullOrWhiteSpace(auth.Audience))
            throw new InvalidOperationException("Auth:Authority and Auth:Audience must be configured.");
        if (auth.RequireHttpsMetadata && !auth.Authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Auth:Authority '{auth.Authority}' is not https while Auth:RequireHttpsMetadata is true; every bearer request would fail. " +
                "Set ASPNETCORE_ENVIRONMENT=Development (enables Auth:RequireHttpsMetadata=false for the local MockEntra) or use an https authority.");
        services.AddSingleton(auth);

        services.AddAuthentication(PolicyScheme)
            .AddPolicyScheme(PolicyScheme, "API key or bearer token", o => o.ForwardDefaultSelector = context =>
                context.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName) ? ApiKeyScheme : BearerScheme)
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyScheme, o =>
                o.Keys = new Dictionary<string, string>(auth.ApiKeys, StringComparer.Ordinal))
            .AddJwtBearer(BearerScheme, o =>
            {
                o.Authority = auth.Authority;
                o.Audience = auth.Audience;
                o.RequireHttpsMetadata = auth.RequireHttpsMetadata;
                o.MapInboundClaims = false; // keep Entra names: roles, tid, azp
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidAudience = auth.Audience,
                    RoleClaimType = "roles",
                    NameClaimType = "azp",
                    AuthenticationType = BearerScheme, // InvokeBearer checks this
                };
                o.Events = new JwtBearerEvents { OnAuthenticationFailed = OnAuthenticationFailed };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(InvokePolicy, p => p.RequireAuthenticatedUser().RequireRole(InvokeRole))
            .AddPolicy(InvokeBearerPolicy, p => p.RequireAuthenticatedUser().RequireRole(InvokeRole)
                .RequireAssertion(c => c.User.Identity?.AuthenticationType == BearerScheme));
        return auth;
    }

    /// <summary>
    /// Bad tokens (signature, expiry, audience) already end as 401 and are not logged here. "Metadata unavailable" (see
    /// <see cref="IsMetadataUnavailable"/>) is logged as a warning; when it is not a token exception it is turned into a failed
    /// authentication so the caller gets 401, not 500. Any other exception follows the normal path: the handler rethrows it,
    /// ASP.NET Core logs it as an error and answers 500.
    /// </summary>
    private static Task OnAuthenticationFailed(AuthenticationFailedContext context)
    {
        var tokenProblem = context.Exception is SecurityTokenException;
        if (!IsMetadataUnavailable(context.Exception)) return Task.CompletedTask;

        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AuthSetup))
            .LogWarning(context.Exception,
                "Bearer token rejected: the OpenID metadata of {Authority} could not be loaded (identity provider down or misconfigured). Answering 401.",
                context.Options.Authority);
        if (!tokenProblem) context.Fail(context.Exception);
        return Task.CompletedTask;
    }

    /// <summary>
    /// True when the exception means the identity provider's OpenID metadata could not be loaded: anywhere in the exception chain
    /// an <see cref="HttpRequestException"/>, an <see cref="IOException"/>, or an <see cref="InvalidOperationException"/> from the
    /// configuration manager (IDX20803 "unable to obtain configuration", IDX20804 "unable to retrieve document"); or IDX10204
    /// (no valid issuer, because the issuer comes only from that metadata).
    /// </summary>
    public static bool IsMetadataUnavailable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is SecurityTokenInvalidIssuerException && exception.Message.Contains("IDX10204", StringComparison.Ordinal))
            return true;
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is HttpRequestException or IOException) return true;
            if (e is InvalidOperationException
                && (e.Message.Contains("IDX20803", StringComparison.Ordinal) || e.Message.Contains("IDX20804", StringComparison.Ordinal)))
                return true;
            if (e is AggregateException aggregate && aggregate.InnerExceptions.Any(IsMetadataUnavailable)) return true;
        }
        return false;
    }
}
