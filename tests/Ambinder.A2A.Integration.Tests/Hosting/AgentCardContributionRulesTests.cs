using System.Text.Json.Nodes;
using Ambinder.A2A.AspNetCore.Hosting;
using Ambinder.A2A.Server.Hosting;

namespace Ambinder.A2A.Integration.Tests.Hosting;

/// <summary>Design D7: an <see cref="IAgentCardContributor"/> may add to the card, never change or remove what is there.</summary>
public class AgentCardContributionRulesTests
{
    private const string Card = """
        {
          "name": "echo",
          "supportedInterfaces": [{ "url": "http://a/jsonrpc", "protocolBinding": "JSONRPC" }],
          "capabilities": { "streaming": true, "extensions": [{ "uri": "urn:a" }] },
          "securitySchemes": { "entra": { "type": "oauth2" } },
          "skills": [{ "id": "s1", "name": "Skill" }]
        }
        """;

    private static readonly Dictionary<string, Action<JsonObject>> AllowedChanges = new()
    {
        ["new top-level member"] = c => c["x-new"] = 1,
        ["appended interface"] = c => c["supportedInterfaces"]!.AsArray().Add(new JsonObject { ["url"] = "http://b" }),
        ["appended extension"] = c => c["capabilities"]!["extensions"]!.AsArray().Add(new JsonObject { ["uri"] = "urn:b" }),
        ["new security scheme"] = c => c["securitySchemes"]!["apiKey"] = new JsonObject { ["type"] = "apiKey" },
        ["card requirements where none"] = c => c["securityRequirements"] = new JsonArray(new JsonObject()),
        ["skill requirements where none"] = c => c["skills"]![0]!["securityRequirements"] = new JsonArray(new JsonObject()),
    };

    private static readonly Dictionary<string, Action<JsonObject>> ForbiddenChanges = new()
    {
        ["changed member"] = c => c["name"] = "other",
        ["removed member"] = c => c.Remove("name"),
        ["changed interface"] = c => c["supportedInterfaces"]![0]!["url"] = "http://evil",
        ["interface inserted first"] = c => c["supportedInterfaces"]!.AsArray().Insert(0, new JsonObject { ["url"] = "http://b" }),
        ["changed capability"] = c => c["capabilities"]!["streaming"] = false,
        ["new capability"] = c => c["capabilities"]!["pushNotifications"] = true,
        ["replaced security scheme"] = c => c["securitySchemes"]!["entra"] = new JsonObject { ["type"] = "apiKey" },
        ["changed skill"] = c => c["skills"]![0]!["name"] = "Other",
        ["added skill"] = c => c["skills"]!.AsArray().Add(new JsonObject { ["id"] = "s2" }),
    };

    public static TheoryData<string> Allowed() => new(AllowedChanges.Keys);

    public static TheoryData<string> Forbidden() => new(ForbiddenChanges.Keys);

    [Theory, MemberData(nameof(Allowed))]
    public void Additions_are_allowed(string change) => Assert.Null(Apply(AllowedChanges[change]));

    [Theory, MemberData(nameof(Forbidden))]
    public void Changes_are_violations(string change) => Assert.NotNull(Apply(ForbiddenChanges[change]));

    [Fact]
    public void Existing_security_requirements_may_not_be_replaced()
    {
        var before = JsonNode.Parse("""{ "securityRequirements": [{ "entra": [] }] }""")!.AsObject();
        var after = (JsonObject)before.DeepClone();
        after["securityRequirements"] = new JsonArray(new JsonObject { ["apiKey"] = new JsonArray() });

        Assert.NotNull(AgentCardContributionRules.FindViolation(before, after));
    }

    private static string? Apply(Action<JsonObject> change)
    {
        var before = JsonNode.Parse(Card)!.AsObject();
        var after = (JsonObject)before.DeepClone();
        change(after);
        return AgentCardContributionRules.FindViolation(before, after);
    }
}
