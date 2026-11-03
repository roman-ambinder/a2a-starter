using System.Text.Json;
using System.Text.Json.Serialization;
using A2A.Protocol;

namespace A2A.Protocol.Tests.Json;

/// <summary>
/// A consumer's own source-generated context that lists our models must still compile and write enum names, even though
/// <c>ProtoEnumConverter&lt;T&gt;</c> is internal. This assembly has no <c>InternalsVisibleTo</c> from A2A.Protocol.
/// </summary>
public partial class ConsumerJsonContextTests
{
    [JsonSerializable(typeof(AgentTask))]
    [JsonSerializable(typeof(Message))]
    private sealed partial class ConsumerContext : JsonSerializerContext;

    [Fact]
    public void Consumer_context_writes_enum_names_and_reads_unknown_as_unspecified()
    {
        var message = new Message { MessageId = "m1", Role = Role.User, Parts = [] };
        var json = JsonSerializer.Serialize(message, ConsumerContext.Default.Message);
        Assert.Contains("\"ROLE_USER\"", json);

        var back = JsonSerializer.Deserialize("""{"messageId":"m1","role":"ROLE_FUTURE","parts":[]}""", ConsumerContext.Default.Message);
        Assert.Equal(Role.Unspecified, back!.Role);
    }
}
