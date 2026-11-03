using System.Text.Json;

namespace A2A.Protocol.Tests.Json;

public class ModelJsonTests
{
    [Fact]
    public void Task_state_serializes_as_proto_enum_name()
        => Assert.Equal("\"TASK_STATE_INPUT_REQUIRED\"", JsonSerializer.Serialize(TaskState.InputRequired, A2AJson.Options));

    [Fact]
    public void Text_part_serializes_flat_without_kind()
        => Assert.Equal("""{"text":"hi"}""", JsonSerializer.Serialize(Part.FromText("hi"), A2AJson.Options));

    [Fact]
    public void Raw_part_is_base64()
        => Assert.Contains("\"raw\":\"AQI=\"", JsonSerializer.Serialize(Part.FromRaw([1, 2], "application/octet-stream"), A2AJson.Options));

    [Fact]
    public void Spec_push_example_deserializes()
    {
        var r = JsonSerializer.Deserialize<StreamResponse>("""{"statusUpdate":{"taskId":"t1","contextId":"c1","status":{"state":"TASK_STATE_COMPLETED","timestamp":"2024-03-15T18:30:00Z"}}}""", A2AJson.Options)!;
        Assert.Equal(TaskState.Completed, r.StatusUpdate!.Status.State);
    }

    [Fact]
    public void Part_with_two_contents_fails_validation()
        => Assert.False(new Part { Text = "a", Url = "https://x" }.IsValid);

    [Theory]
    [InlineData("\"working\"")]          // 0.3 spelling: not guessed, read as unspecified
    [InlineData("\"TASK_STATE_FUTURE\"")] // a newer peer's value
    [InlineData("99")]
    public void Unknown_task_state_reads_as_unspecified(string value)
        => Assert.Equal(TaskState.Unspecified,
            JsonSerializer.Deserialize<AgentTaskStatus>($$"""{"state":{{value}}}""", A2AJson.Options)!.State);

    [Fact]
    public void Unknown_role_reads_as_unspecified()
        => Assert.Equal(Role.Unspecified, JsonSerializer.Deserialize<Message>("""{"messageId":"m","role":"user"}""", A2AJson.Options)!.Role);

    [Fact]
    public void Known_integer_enum_values_are_read_and_written_as_names()
    {
        var status = JsonSerializer.Deserialize<AgentTaskStatus>("""{"state":2}""", A2AJson.Options)!;
        Assert.Equal(TaskState.Working, status.State);
        Assert.Equal("""{"state":"TASK_STATE_WORKING"}""", JsonSerializer.Serialize(status, A2AJson.Options));
        Assert.Equal(Role.Agent, JsonSerializer.Deserialize<Message>("""{"messageId":"m","role":2}""", A2AJson.Options)!.Role);
    }

    [Fact]
    public void Enum_of_wrong_json_type_throws_JsonException()
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AgentTaskStatus>("""{"state":true}""", A2AJson.Options));

    [Fact]
    public void Timestamp_serializes_as_utc_iso8601()
    {
        var s = new AgentTaskStatus { State = TaskState.Working, Timestamp = new DateTimeOffset(2024, 3, 15, 18, 30, 0, TimeSpan.Zero) };
        Assert.Equal("""{"state":"TASK_STATE_WORKING","timestamp":"2024-03-15T18:30:00Z"}""", JsonSerializer.Serialize(s, A2AJson.Options));
    }

    [Fact]
    public void Empty_lists_and_nulls_are_omitted()
        => Assert.Equal("""{"messageId":"m","role":"ROLE_USER"}""",
            JsonSerializer.Serialize(new Message { MessageId = "m", Role = Role.User }, A2AJson.Options));

    [Fact]
    public void Unknown_fields_are_ignored_by_default_but_missing_lists_are_empty()
    {
        var m = JsonSerializer.Deserialize<Message>("""{"messageId":"m","role":"ROLE_AGENT"}""", A2AJson.Options)!;
        Assert.Empty(m.Parts);
        Assert.Equal(Role.Agent, m.Role);
    }

    [Fact]
    public void Part_helpers_and_effective_media_type()
    {
        Assert.Equal("text/plain", Part.FromText("a").EffectiveMediaType);
        Assert.Equal("application/json", Part.FromData(JsonDocument.Parse("{}").RootElement).EffectiveMediaType);
        Assert.Equal("image/png", Part.FromUrl("https://x/y.png", "image/png").EffectiveMediaType);
        Assert.Equal("application/octet-stream", new Part { Url = "u" }.EffectiveMediaType);
        Assert.True(Part.FromText("a").IsValid);
        Assert.False(new Part().IsValid);
    }

    [Fact]
    public void Security_scheme_uses_proto_json_names()
    {
        var card = new SecurityScheme
        {
            Oauth2SecurityScheme = new OAuth2SecurityScheme
            {
                Oauth2MetadataUrl = "https://m",
                Flows = new OAuthFlows
                {
                    ClientCredentials = new ClientCredentialsOAuthFlow
                    {
                        TokenUrl = "https://t",
                        Scopes = new Dictionary<string, string> { ["s"] = "d" },
                    },
                },
            },
        };
        Assert.Equal(
            """{"oauth2SecurityScheme":{"flows":{"clientCredentials":{"tokenUrl":"https://t","scopes":{"s":"d"}}},"oauth2MetadataUrl":"https://m"}}""",
            JsonSerializer.Serialize(card, A2AJson.Options));
        var m = new SecurityScheme { MtlsSecurityScheme = new MutualTlsSecurityScheme() };
        Assert.Equal("""{"mtlsSecurityScheme":{}}""", JsonSerializer.Serialize(m, A2AJson.Options));
    }

    [Fact]
    public void Agent_card_round_trips()
    {
        var card = new AgentCard
        {
            Name = "n", Description = "d", Version = "1",
            SupportedInterfaces = [new AgentInterface { Url = "http://x", ProtocolBinding = A2AConstants.BindingRest, ProtocolVersion = "1.0" }],
            Capabilities = new AgentCapabilities { Streaming = true },
            DefaultInputModes = ["text/plain"], DefaultOutputModes = ["text/plain"],
            Skills = [new AgentSkill { Id = "i", Name = "n", Description = "d", Tags = ["t"] }],
            SecuritySchemes = new Dictionary<string, SecurityScheme> { ["k"] = new() { ApiKeySecurityScheme = new ApiKeySecurityScheme { Location = "header", Name = "X-API-Key" } } },
            SecurityRequirements = [new SecurityRequirement { Schemes = new Dictionary<string, StringList> { ["k"] = new() { List = ["a"] } } }],
        };
        var json = JsonSerializer.Serialize(card, A2AJson.Options);
        Assert.Contains("\"supportedInterfaces\"", json);
        Assert.Contains("\"securityRequirements\":[{\"schemes\":{\"k\":{\"list\":[\"a\"]}}}]", json);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<AgentCard>(json, A2AJson.Options), A2AJson.Options));
    }

    [Fact]
    public void Missing_members_keep_non_null_defaults()
    {
        var t = JsonSerializer.Deserialize<AgentTask>("{}", A2AJson.Options)!;
        Assert.Equal("", t.Id);
        Assert.NotNull(t.Status);
        Assert.Empty(t.Artifacts);
    }

    private sealed class PlainList<T>(params T[] items) : IReadOnlyList<T>
    {
        public T this[int index] => items[index];
        public int Count => items.Length;
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void Non_collection_read_only_list_is_serialized_when_non_empty_and_omitted_when_empty()
    {
        Assert.Equal("""{"messageId":"m","role":"ROLE_USER","extensions":["e"]}""",
            JsonSerializer.Serialize(new Message { MessageId = "m", Role = Role.User, Extensions = new PlainList<string>("e") }, A2AJson.Options));
        Assert.Equal("""{"messageId":"m","role":"ROLE_USER"}""",
            JsonSerializer.Serialize(new Message { MessageId = "m", Role = Role.User, Extensions = new PlainList<string>() }, A2AJson.Options));
    }
}
