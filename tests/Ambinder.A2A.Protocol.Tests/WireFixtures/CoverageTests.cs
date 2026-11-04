using Ambinder.A2A.Tests.WireFixtures;

namespace Ambinder.A2A.Protocol.Tests.WireFixtures;

public class CoverageTests
{
    static readonly ProtoInventory Proto = ProtoInventory.Load(Path.Combine(AppContext.BaseDirectory, "a2a.proto"));
    static readonly IReadOnlyList<WireFixture> Fixtures = FixtureCatalog.Load(FixtureCatalog.Root);

    [Fact]
    public void Every_proto_message_is_mapped_or_explained()
    {
        var unmapped = Proto.Messages.Where(m => !WireTypes.ByProtoName.ContainsKey(m) && !WireTypes.NotStandalone.ContainsKey(m)).ToList();
        Assert.True(unmapped.Count == 0, "Add to WireTypes: " + string.Join(", ", unmapped));
    }

    [Fact]
    public void Every_mapped_proto_message_has_a_roundtrip_fixture()
    {
        var covered = Fixtures.Where(f => f.Meta.Expect == FixtureExpect.Roundtrip).Select(f => f.ProtoType).ToHashSet();
        var missing = WireTypes.ByProtoName.Keys.Where(m => !covered.Contains(m) && !PendingCoverage.Contains(m)).ToList();
        Assert.True(missing.Count == 0, "No fixture for: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_enum_value_appears_in_an_enum_fixture()
    {
        foreach (var (name, values) in Proto.Enums)
        {
            if (PendingCoverage.Contains(name)) continue;
            var fixture = Fixtures.SingleOrDefault(f => f.Id == $"enums/{name}");
            Assert.True(fixture is not null, $"missing enums/{name}.json");
            var json = fixture!.ReadJson();
            foreach (var v in values) Assert.Contains($"\"{v}\"", json);
        }
    }

    /// <summary>
    /// Shrinks to empty during Tasks 4-6; Task 6 deletes it. Visible on purpose: not a skip list.
    /// </summary>
    static readonly HashSet<string> PendingCoverage =
    [
        "AuthenticationInfo",
        "AgentInterface",
        "AgentCard",
        "AgentProvider",
        "AgentCapabilities",
        "AgentExtension",
        "AgentSkill",
        "AgentCardSignature",
        "TaskPushNotificationConfig",
        "StringList",
        "SecurityRequirement",
        "SecurityScheme",
        "APIKeySecurityScheme",
        "HTTPAuthSecurityScheme",
        "OAuth2SecurityScheme",
        "OpenIdConnectSecurityScheme",
        "MutualTlsSecurityScheme",
        "OAuthFlows",
        "AuthorizationCodeOAuthFlow",
        "ClientCredentialsOAuthFlow",
        "ImplicitOAuthFlow",
        "PasswordOAuthFlow",
        "DeviceCodeOAuthFlow",
        "SendMessageRequest",
        "GetTaskRequest",
        "ListTasksRequest",
        "ListTasksResponse",
        "CancelTaskRequest",
        "GetTaskPushNotificationConfigRequest",
        "DeleteTaskPushNotificationConfigRequest",
        "SubscribeToTaskRequest",
        "ListTaskPushNotificationConfigsRequest",
        "GetExtendedAgentCardRequest",
        "ListTaskPushNotificationConfigsResponse",
        "TaskState",
        "Role"
    ];
}
