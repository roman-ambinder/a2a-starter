using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client.Tests.TestDoubles;

/// <summary>Builders for agent cards.</summary>
public static class Cards
{
    public static AgentCard Single(string binding, string version, string url, string? tenant = null) => new()
    {
        Name = "test-agent",
        Description = "Test agent",
        Version = "1.0.0",
        SupportedInterfaces = [new AgentInterface { Url = url, ProtocolBinding = binding, ProtocolVersion = version, Tenant = tenant }],
        Capabilities = new AgentCapabilities { Streaming = true },
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
    };

    public static AgentCard JsonRpc(string url, string? tenant = null) => Single(A2AConstants.BindingJsonRpc, A2AConstants.ProtocolVersion, url, tenant);

    public static AgentCard Rest(string url) => Single(A2AConstants.BindingRest, A2AConstants.ProtocolVersion, url);

    public static AgentCard Grpc(string url) => Single(A2AConstants.BindingGrpc, A2AConstants.ProtocolVersion, url);

    public static AgentCard Streaming(this AgentCard card, bool enabled) =>
        card with { Capabilities = card.Capabilities with { Streaming = enabled } };

    public static AgentCard WithExtended(this AgentCard card) =>
        card with { Capabilities = card.Capabilities with { ExtendedAgentCard = true } };

    public static AgentCard WithSecurity(this AgentCard card, IReadOnlyDictionary<string, SecurityScheme> schemes,
        params SecurityRequirement[] requirements) =>
        card with { SecuritySchemes = schemes, SecurityRequirements = requirements };

    /// <summary>A card in the 0.3 wire format (flat <c>url</c> and <c>preferredTransport</c>).</summary>
    public const string V0_3Json = """
        {
          "protocolVersion": "0.3.0",
          "name": "legacy-agent",
          "description": "A 0.3 agent",
          "url": "https://legacy.example.com/a2a",
          "preferredTransport": "JSONRPC",
          "version": "1.0.0",
          "capabilities": { "streaming": true },
          "defaultInputModes": ["text/plain"],
          "defaultOutputModes": ["text/plain"],
          "skills": []
        }
        """;
}

/// <summary>Builders for messages.</summary>
public static class Msg
{
    public static Message Agent(string text) => Make(Role.Agent, text);

    public static Message User(string text) => Make(Role.User, text);

    private static Message Make(Role role, string text) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = role,
        Parts = [Part.FromText(text)],
    };
}
