using System.Text.Json;

namespace AgentClient;

/// <summary>How the client authenticates.</summary>
public enum AuthMode
{
    /// <summary>No credentials.</summary>
    None,
    /// <summary>API key header (<c>X-API-Key</c>).</summary>
    ApiKey,
    /// <summary>OAuth client credentials against MockEntra.</summary>
    Mock,
    /// <summary>Real Entra ID through <c>DefaultAzureCredential</c> (not tested).</summary>
    Entra,
}

/// <summary>A client id and secret of a MockEntra application.</summary>
public sealed record ClientSecret(string Id, string Secret);

/// <summary>The identities the flows act as.</summary>
public enum Identity
{
    /// <summary>Role A2A.Invoke.</summary>
    Client,
    /// <summary>Roles A2A.Invoke and Reports.Approve.</summary>
    Approver,
    /// <summary>No roles.</summary>
    NoRoles,
}

/// <summary>Everything a <see cref="FlowRunner"/> needs. Defaults are the local dev values.</summary>
public sealed record FlowOptions
{
    public string ServerUrl { get; init; } = "http://localhost:5200";

    /// <summary>Agent to talk to. Null = the default of each flow.</summary>
    public string? Agent { get; init; }

    /// <summary><c>JSONRPC</c>, <c>HTTP+JSON</c> or <c>GRPC</c>. Null = pick from the card.</summary>
    public string? Binding { get; init; }

    public AuthMode Auth { get; init; } = AuthMode.Mock;
    public bool Verbose { get; init; }
    public string ReceiverUrl { get; init; } = "http://localhost:5300";
    public string MockEntraUrl { get; init; } = "http://localhost:5100";
    public string TenantId { get; init; } = "a2a00000-0000-0000-0000-000000000001";

    /// <summary>OAuth scope for <see cref="AuthMode.Mock"/> and <see cref="AuthMode.Entra"/>.</summary>
    public string Scope { get; init; } = "api://a2a-demo/.default";

    public string ApiKey { get; init; } = "dev-api-key";
    public ClientSecret Client { get; init; } = new("a2a-client", "dev-client-secret");
    public ClientSecret Approver { get; init; } = new("a2a-approver", "dev-approver-secret");
    public ClientSecret NoRoles { get; init; } = new("a2a-noroles", "dev-noroles-secret");
    public TextWriter Output { get; init; } = Console.Out;

    public string TokenUrl => $"{MockEntraUrl.TrimEnd('/')}/{TenantId}/oauth2/v2.0/token";

    /// <summary>Maps <c>jsonrpc|rest|grpc|auto</c> to a card binding name (null for auto).</summary>
    public static string? ParseBinding(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "auto" => null,
        "jsonrpc" => "JSONRPC",
        "rest" => "HTTP+JSON",
        "grpc" => "GRPC",
        _ => throw new ArgumentException($"Unknown transport '{value}'. Use jsonrpc, rest, grpc or auto."),
    };

    /// <summary>Maps <c>none|apikey|mock|entra</c>.</summary>
    public static AuthMode ParseAuth(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "none" => AuthMode.None,
        "apikey" => AuthMode.ApiKey,
        null or "" or "mock" => AuthMode.Mock,
        "entra" => AuthMode.Entra,
        _ => throw new ArgumentException($"Unknown auth mode '{value}'. Use none, apikey, mock or entra."),
    };

    /// <summary>Applies <c>appsettings.json</c> (if found) on top of the defaults.</summary>
    public static FlowOptions FromSettings(string? path = null)
    {
        path ??= new[]
        {
            Path.Combine(AppContext.BaseDirectory, "agentclient-config", "appsettings.json"),
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
        }.FirstOrDefault(File.Exists);
        var options = new FlowOptions();
        if (path is null || !File.Exists(path)) return options;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        ClientSecret? Secret(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var c) && Str(c, "Id") is { } id && Str(c, "Secret") is { } s ? new ClientSecret(id, s) : null;

        var entra = root.TryGetProperty("MockEntra", out var m) ? m : default;
        return options with
        {
            ServerUrl = Str(root, "ServerUrl") ?? options.ServerUrl,
            ReceiverUrl = Str(root, "ReceiverUrl") ?? options.ReceiverUrl,
            ApiKey = Str(root, "ApiKey") ?? options.ApiKey,
            MockEntraUrl = Str(entra, "BaseUrl") ?? options.MockEntraUrl,
            TenantId = Str(entra, "TenantId") ?? options.TenantId,
            Scope = Str(entra, "Scope") ?? options.Scope,
            Client = Secret(entra, "Client") ?? options.Client,
            Approver = Secret(entra, "Approver") ?? options.Approver,
            NoRoles = Secret(entra, "NoRoles") ?? options.NoRoles,
        };
    }
}

/// <summary>The outcome of one flow.</summary>
public sealed record FlowResult(string Flow, bool Passed, IReadOnlyList<string> Steps);
