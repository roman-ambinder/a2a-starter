using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using A2A.Protocol;

namespace A2A.Protocol.Tests.Json;

/// <summary>
/// A consumer's own source-generated context that lists our models must still compile and write enum names. This works only
/// because <c>ProtoEnumConverter&lt;T&gt;</c> is public: our enums name it in <c>[JsonConverter]</c>, and an internal converter
/// breaks such a context (SYSLIB1220, SYSLIB1030). It is hidden from IntelliSense instead (owner decision 2026-11-04).
/// This assembly has no <c>InternalsVisibleTo</c> from A2A.Protocol.
/// </summary>
public partial class ConsumerJsonContextTests
{
    [JsonSerializable(typeof(AgentTask))]
    [JsonSerializable(typeof(Message))]
    [JsonSerializable(typeof(AgentCard))]
    private sealed partial class ConsumerContext : JsonSerializerContext;

    [Fact]
    public void Proto_enum_converter_is_public_and_hidden_from_IntelliSense()
    {
        var type = typeof(Role).Assembly.GetType("A2A.Protocol.ProtoEnumConverter`1");
        Assert.NotNull(type);
        Assert.True(type!.IsPublic);
        Assert.True(type.IsGenericTypeDefinition);
        var browsable = type.GetCustomAttribute<EditorBrowsableAttribute>();
        Assert.NotNull(browsable);
        Assert.Equal(EditorBrowsableState.Never, browsable!.State);
    }

    [Fact]
    public void Consumer_context_writes_enum_names_and_reads_unknown_as_unspecified()
    {
        var message = new Message { MessageId = "m1", Role = Role.User, Parts = [] };
        var json = JsonSerializer.Serialize(message, ConsumerContext.Default.Message);
        Assert.Contains("\"ROLE_USER\"", json);

        var back = JsonSerializer.Deserialize("""{"messageId":"m1","role":"ROLE_FUTURE","parts":[]}""", ConsumerContext.Default.Message);
        Assert.Equal(Role.Unspecified, back!.Role);
    }

    [Fact]
    public void Consumer_context_round_trips_a_card_with_an_oauth_flow()
    {
        var card = new AgentCard
        {
            Name = "echo",
            SecuritySchemes = new Dictionary<string, SecurityScheme>
            {
                ["oauth"] = new()
                {
                    Oauth2SecurityScheme = new()
                    {
                        Flows = new()
                        {
                            ClientCredentials = new() { TokenUrl = "https://idp.example/token", Scopes = new Dictionary<string, string> { ["a2a"] = "Call the agent" } },
                        },
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(card, ConsumerContext.Default.AgentCard);
        var back = JsonSerializer.Deserialize(json, ConsumerContext.Default.AgentCard)!;

        Assert.Equal("https://idp.example/token", back.SecuritySchemes!["oauth"].Oauth2SecurityScheme!.Flows.ClientCredentials!.TokenUrl);
        Assert.Equal("Call the agent", back.SecuritySchemes["oauth"].Oauth2SecurityScheme!.Flows.ClientCredentials!.Scopes["a2a"]);
    }
}
