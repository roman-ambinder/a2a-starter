using System.Text.Json;

namespace A2A.Protocol.Tests;

/// <summary>One fully populated instance of every top-level model. Every field is set so name drift cannot hide.</summary>
public static class Samples
{
    private static readonly DateTimeOffset T1 = new(2026, 11, 2, 10, 30, 15, 250, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 11, 2, 11, 0, 0, TimeSpan.Zero);

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement Meta(string key) => J("{\"" + key + "\":\"v\",\"n\":1.5,\"flag\":true,\"list\":[1,\"two\"],\"nested\":{\"a\":\"b\"}}");

    public static Part TextPart() => new() { Text = "hello", Metadata = Meta("t"), Filename = "a.txt", MediaType = "text/plain" };
    public static Part RawPart() => new() { Raw = [1, 2, 3, 250], Metadata = Meta("r"), Filename = "a.bin", MediaType = "application/octet-stream" };
    public static Part UrlPart() => new() { Url = "https://example.com/f.png", Metadata = Meta("u"), Filename = "f.png", MediaType = "image/png" };
    public static Part DataPart() => new() { Data = J("""{"k":[1,2,{"x":"y"}],"b":false}"""), Metadata = Meta("d"), Filename = "d.json", MediaType = "application/json" };

    public static Message FullMessage() => new()
    {
        MessageId = "m-1", ContextId = "ctx-1", TaskId = "t-1", Role = Role.User,
        Parts = [TextPart(), RawPart(), UrlPart(), DataPart()],
        Metadata = Meta("m"), Extensions = ["https://ext.example/a", "https://ext.example/b"], ReferenceTaskIds = ["t-0", "t-00"],
    };

    public static Artifact FullArtifact() => new()
    {
        ArtifactId = "art-1", Name = "report", Description = "A report", Parts = [TextPart(), DataPart()],
        Metadata = Meta("a"), Extensions = ["https://ext.example/a"],
    };

    public static AgentTaskStatus FullStatus() => new()
    {
        State = TaskState.InputRequired, Timestamp = T1,
        Message = new Message { MessageId = "m-s", Role = Role.Agent, Parts = [Part.FromText("need more")] },
    };

    public static AgentTask FullTask() => new()
    {
        Id = "t-1", ContextId = "ctx-1", Status = FullStatus(), Artifacts = [FullArtifact()],
        History = [FullMessage()], Metadata = Meta("task"),
    };

    public static TaskPushNotificationConfig FullPushConfig() => new()
    {
        Tenant = "acme", Id = "cfg-1", TaskId = "t-1", Url = "https://hook.example/cb", Token = "tok",
        Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = "secret" },
    };

    private static readonly Dictionary<string, string> Scopes = new() { ["read"] = "Read access", ["write"] = "Write access" };

    public static AgentCard FullCard(OAuthFlows? flows = null) => new()
    {
        Name = "Echo", Description = "Echo agent",
        SupportedInterfaces =
        [
            new AgentInterface { Url = "http://localhost:5200/agents/echo/jsonrpc", ProtocolBinding = "JSONRPC", Tenant = "acme", ProtocolVersion = "1.0" },
            new AgentInterface { Url = "http://localhost:5201", ProtocolBinding = "GRPC", ProtocolVersion = "1.0" },
        ],
        Provider = new AgentProvider { Url = "https://example.com", Organization = "Example" },
        Version = "1.2.3", DocumentationUrl = "https://example.com/docs", IconUrl = "https://example.com/i.png",
        Capabilities = new AgentCapabilities
        {
            Streaming = true, PushNotifications = false, ExtendedAgentCard = true,
            Extensions = [new AgentExtension { Uri = "https://ext.example/a", Description = "ext", Required = true, Params = Meta("p") }],
        },
        SecuritySchemes = new Dictionary<string, SecurityScheme>
        {
            ["apiKey"] = new() { ApiKeySecurityScheme = new ApiKeySecurityScheme { Description = "key", Location = "header", Name = "X-API-Key" } },
            ["http"] = new() { HttpAuthSecurityScheme = new HttpAuthSecurityScheme { Description = "bearer", Scheme = "Bearer", BearerFormat = "JWT" } },
            ["oauth"] = new() { Oauth2SecurityScheme = new OAuth2SecurityScheme { Description = "oauth", Flows = flows ?? AuthCodeFlows(), Oauth2MetadataUrl = "https://idp.example/.well-known/oauth-authorization-server" } },
            ["oidc"] = new() { OpenIdConnectSecurityScheme = new OpenIdConnectSecurityScheme { Description = "oidc", OpenIdConnectUrl = "https://idp.example/.well-known/openid-configuration" } },
            ["mtls"] = new() { MtlsSecurityScheme = new MutualTlsSecurityScheme { Description = "mtls" } },
        },
        SecurityRequirements =
        [
            new SecurityRequirement { Schemes = new Dictionary<string, StringList> { ["oauth"] = new() { List = ["read", "write"] }, ["apiKey"] = new() { List = [] } } },
        ],
        DefaultInputModes = ["text/plain"], DefaultOutputModes = ["text/plain", "application/json"],
        Skills =
        [
            new AgentSkill
            {
                Id = "echo", Name = "Echo", Description = "Echoes", Tags = ["echo", "test"], Examples = ["hi"],
                InputModes = ["text/plain"], OutputModes = ["text/plain"],
                SecurityRequirements = [new SecurityRequirement { Schemes = new Dictionary<string, StringList> { ["http"] = new() { List = ["x"] } } }],
            },
        ],
        Signatures = [new AgentCardSignature { Protected = "eyJhbGciOiJFUzI1NiJ9", Signature = "c2ln", Header = Meta("sig") }],
    };

    public static OAuthFlows AuthCodeFlows() => new()
    {
        AuthorizationCode = new AuthorizationCodeOAuthFlow
        {
            AuthorizationUrl = "https://idp.example/authorize", TokenUrl = "https://idp.example/token",
            RefreshUrl = "https://idp.example/refresh", Scopes = Scopes, PkceRequired = true,
        },
    };

    public static OAuthFlows ClientCredentialsFlows() => new()
    {
        ClientCredentials = new ClientCredentialsOAuthFlow
        {
            TokenUrl = "https://idp.example/token", RefreshUrl = "https://idp.example/refresh", Scopes = Scopes,
        },
    };

#pragma warning disable CS0618 // deprecated OAuth flows: built here to test that they round-trip
    public static OAuthFlows ImplicitFlows() => new()
    {
        Implicit = new ImplicitOAuthFlow { AuthorizationUrl = "https://idp.example/authorize", RefreshUrl = "https://idp.example/refresh", Scopes = Scopes },
    };

    public static OAuthFlows PasswordFlows() => new()
    {
        Password = new PasswordOAuthFlow { TokenUrl = "https://idp.example/token", RefreshUrl = "https://idp.example/refresh", Scopes = Scopes },
    };
#pragma warning restore CS0618

    public static OAuthFlows DeviceCodeFlows() => new()
    {
        DeviceCode = new DeviceCodeOAuthFlow
        {
            DeviceAuthorizationUrl = "https://idp.example/device", TokenUrl = "https://idp.example/token",
            RefreshUrl = "https://idp.example/refresh", Scopes = Scopes,
        },
    };

    public static TheoryData<object> All()
    {
        var task = FullTask();
        var message = FullMessage();
        var status = new TaskStatusUpdateEvent { TaskId = "t-1", ContextId = "ctx-1", Status = FullStatus(), Metadata = Meta("su") };
        var artifactUpdate = new TaskArtifactUpdateEvent
        {
            TaskId = "t-1", ContextId = "ctx-1", Artifact = FullArtifact(), Append = true, LastChunk = true, Metadata = Meta("au"),
        };
        var push = FullPushConfig();

        object[] items =
        [
            task, FullStatus(), message, TextPart(), RawPart(), UrlPart(), DataPart(), FullArtifact(), status, artifactUpdate,
            new StreamResponse { Task = task }, new StreamResponse { Message = message },
            new StreamResponse { StatusUpdate = status }, new StreamResponse { ArtifactUpdate = artifactUpdate },
            new SendMessageResponse { Task = task }, new SendMessageResponse { Message = message },
            new SendMessageConfiguration { AcceptedOutputModes = ["text/plain", "application/json"], TaskPushNotificationConfig = push, HistoryLength = 5, ReturnImmediately = true },
            new SendMessageRequest
            {
                Tenant = "acme", Message = message, Metadata = Meta("req"),
                Configuration = new SendMessageConfiguration { AcceptedOutputModes = ["text/plain"], TaskPushNotificationConfig = push, HistoryLength = 3, ReturnImmediately = true },
            },
            new GetTaskRequest { Tenant = "acme", Id = "t-1", HistoryLength = 2 },
            new ListTasksRequest
            {
                Tenant = "acme", ContextId = "ctx-1", Status = TaskState.Working, PageSize = 10, PageToken = "next",
                HistoryLength = 1, StatusTimestampAfter = T2, IncludeArtifacts = true,
            },
            new ListTasksResponse { Tasks = [task], NextPageToken = "tok", PageSize = 10, TotalSize = 42 },
            new CancelTaskRequest { Tenant = "acme", Id = "t-1", Metadata = Meta("c") },
            new SubscribeToTaskRequest { Tenant = "acme", Id = "t-1" },
            push, push.Authentication!,
            new GetTaskPushNotificationConfigRequest { Tenant = "acme", TaskId = "t-1", Id = "cfg-1" },
            new DeleteTaskPushNotificationConfigRequest { Tenant = "acme", TaskId = "t-1", Id = "cfg-1" },
            new ListTaskPushNotificationConfigsRequest { Tenant = "acme", TaskId = "t-1", PageSize = 5, PageToken = "p" },
            new ListTaskPushNotificationConfigsResponse { Configs = [push], NextPageToken = "n" },
            new GetExtendedAgentCardRequest { Tenant = "acme" },
            FullCard(), FullCard(ClientCredentialsFlows()), FullCard(DeviceCodeFlows()), FullCard(ImplicitFlows()), FullCard(PasswordFlows()),
            FullCard().SupportedInterfaces[0], FullCard().Provider!, FullCard().Capabilities, FullCard().Capabilities.Extensions[0],
            FullCard().Skills[0], FullCard().Signatures[0], FullCard().SecurityRequirements[0],
            new StringList { List = ["a", "b"] }, AuthCodeFlows(), ClientCredentialsFlows(), DeviceCodeFlows(), ImplicitFlows(), PasswordFlows(),
            FullCard().SecuritySchemes!["apiKey"], FullCard().SecuritySchemes!["http"], FullCard().SecuritySchemes!["oauth"],
            FullCard().SecuritySchemes!["oidc"], FullCard().SecuritySchemes!["mtls"],
        ];

        var data = new TheoryData<object>();
        foreach (var item in items) data.Add(item);
        return data;
    }
}
