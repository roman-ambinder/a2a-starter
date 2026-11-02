using A2A.Protocol;
using A2A.Server.Hosting;
using AgentServer.Auth;

namespace AgentServer.Cards;

/// <summary>
/// The four demo agent cards. Hosting replaces <c>supportedInterfaces</c>; everything else is defined here.
/// <list type="table">
/// <item>echo: open; streaming; text, JSON and any file; extension timestamp (optional).</item>
/// <item>report: Invoke; entra OR apiKey (apiKey only when keys are configured); streaming, push, extended card (+ skill <c>report-archive</c>).</item>
/// <item>booking: Invoke; entra OR apiKey (apiKey only when keys are configured); streaming, push.</item>
/// <item>secure: InvokeBearer; entra; streaming, push, extended card (+ skill <c>audit-log</c>); extension audit (required).</item>
/// </list>
/// </summary>
public static class AgentCards
{
    public const string Echo = "echo";
    public const string Report = "report";
    public const string Booking = "booking";
    public const string Secure = "secure";

    public static readonly IReadOnlyList<string> Names = [Echo, Report, Booking, Secure];

    public const string TimestampExtension = "https://example.com/a2a/ext/timestamp/v1";
    public const string AuditExtension = "https://example.com/a2a/ext/audit/v1";

    public const string EntraScheme = "entra";
    public const string EntraOidcScheme = "entraOidc";
    public const string ApiKeyScheme = "apiKey";

    /// <summary>Authorization policy per agent (null = open).</summary>
    public static string? PolicyFor(string name) => name switch
    {
        Echo => null,
        Report or Booking => AuthSetup.InvokePolicy,
        Secure => AuthSetup.InvokeBearerPolicy,
        _ => throw Unknown(name),
    };

    /// <summary>True for agents that have an extended card.</summary>
    public static bool HasExtendedCard(string name) => name is Report or Secure;

    public static AgentCard Build(string name, A2AServerOptions server, AuthOptions auth, bool extended)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(auth);
        var card = name switch
        {
            Echo => new AgentCard
            {
                Name = Echo,
                Description = "Echoes text, JSON data and files back. Open to anonymous callers.",
                Capabilities = new AgentCapabilities
                {
                    Streaming = true, PushNotifications = false, ExtendedAgentCard = false,
                    Extensions = [new AgentExtension { Uri = TimestampExtension, Description = "Adds a server timestamp to the reply metadata.", Required = false }],
                },
                DefaultInputModes = ["text/plain", "application/json", "*/*"],
                DefaultOutputModes = ["text/plain", "application/json", "*/*"],
                Skills = [Skill("echo", "Echo", "Returns the message parts unchanged.", ["echo", "test"], ["hello"])],
            },
            Report => Protected(Report, "Writes a short report on a topic, streamed in chunks, with summary and CSV data.", auth,
                bearerOnly: false, extendedCard: true,
                Skill("generate-report", "Generate report", "Writes a markdown report about a topic.", ["report"], ["Q3 sales in Europe"])),
            Booking => Protected(Booking, "Books a flight in a short conversation (city, then date).", auth,
                bearerOnly: false, extendedCard: false,
                Skill("book-flight", "Book flight", "Books a flight to Paris, London or Tokyo.", ["booking", "travel"], ["book Paris"])),
            Secure => Protected(Secure, "Approves reports. Bearer tokens only; approvals need role Reports.Approve.", auth,
                bearerOnly: true, extendedCard: true,
                Skill("approve-report", "Approve report", "Approves a report by id.", ["approval"], ["approve R-1"])) with
                {
                    Capabilities = new AgentCapabilities
                    {
                        Streaming = true, PushNotifications = true, ExtendedAgentCard = true,
                        Extensions = [new AgentExtension { Uri = AuditExtension, Description = "Caller accepts audit logging of every request.", Required = true }],
                    },
                },
            _ => throw Unknown(name),
        };

        card = card with
        {
            Version = "1.0.0",
            Provider = new AgentProvider { Organization = "A2A .NET reference", Url = server.PublicBaseUrl.TrimEnd('/') },
        };
        if (!extended) return card;

        return name switch
        {
            Report => card with
            {
                Description = card.Description + " Extended card: archive access.",
                Skills = [.. card.Skills, Skill("report-archive", "Report archive", "Lists and fetches earlier reports.", ["report", "archive"], ["list reports"])],
            },
            Secure => card with
            {
                Description = card.Description + " Extended card: audit log access.",
                Skills = [.. card.Skills, Skill("audit-log", "Audit log", "Shows the audit log of approvals.", ["audit"], ["show audit log"])],
            },
            _ => throw new ArgumentException($"Agent '{name}' has no extended card.", nameof(extended)),
        };
    }

    /// <summary>
    /// The <c>apiKey</c> scheme and requirement appear only when the agent accepts API keys (<paramref name="bearerOnly"/> false)
    /// and at least one key is configured, so a card never advertises a scheme the server would refuse.
    /// </summary>
    private static AgentCard Protected(string name, string description, AuthOptions auth, bool bearerOnly, bool extendedCard, AgentSkill skill)
    {
        var apiKey = !bearerOnly && auth.ApiKeys.Values.Any(k => !string.IsNullOrEmpty(k));
        return new()
        {
            Name = name,
            Description = description,
            Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = true, ExtendedAgentCard = extendedCard },
            SecuritySchemes = Schemes(auth, apiKey),
            SecurityRequirements = apiKey
                ? [Requirement(EntraScheme, auth.Scope), Requirement(ApiKeyScheme)]
                : [Requirement(EntraScheme, auth.Scope)],
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["text/plain", "text/markdown", "application/json"],
            Skills = [skill],
        };
    }

    private static Dictionary<string, SecurityScheme> Schemes(AuthOptions auth, bool apiKey)
    {
        var schemes = new Dictionary<string, SecurityScheme>
        {
            [EntraScheme] = new SecurityScheme
            {
                Oauth2SecurityScheme = new OAuth2SecurityScheme
                {
                    Description = "Microsoft Entra ID client credentials (app roles: A2A.Invoke).",
                    Flows = new OAuthFlows
                    {
                        ClientCredentials = new ClientCredentialsOAuthFlow
                        {
                            TokenUrl = auth.TokenUrl,
                            Scopes = new Dictionary<string, string> { [auth.Scope] = "Invoke A2A demo agents" },
                        },
                    },
                },
            },
            [EntraOidcScheme] = new SecurityScheme
            {
                OpenIdConnectSecurityScheme = new OpenIdConnectSecurityScheme
                {
                    Description = "Microsoft Entra ID OpenID Connect metadata.",
                    OpenIdConnectUrl = auth.OpenIdConfigurationUrl,
                },
            },
        };
        if (apiKey)
        {
            schemes[ApiKeyScheme] = new SecurityScheme
            {
                ApiKeySecurityScheme = new ApiKeySecurityScheme
                {
                    Description = "API key.",
                    Location = "header",
                    Name = ApiKeyAuthenticationHandler.HeaderName,
                },
            };
        }
        return schemes;
    }

    private static SecurityRequirement Requirement(string scheme, params string[] scopes)
        => new() { Schemes = new Dictionary<string, StringList> { [scheme] = new StringList { List = scopes } } };

    private static AgentSkill Skill(string id, string name, string description, string[] tags, string[] examples)
        => new() { Id = id, Name = name, Description = description, Tags = tags, Examples = examples };

    private static ArgumentException Unknown(string name) => new($"Unknown agent '{name}'.", nameof(name));
}
