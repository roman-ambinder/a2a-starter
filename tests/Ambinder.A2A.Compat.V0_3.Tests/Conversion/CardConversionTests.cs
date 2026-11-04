using System.Text.Json;
using System.Text.Json.Nodes;
using Ambinder.A2A.Compat.V0_3.Shared.Conversion;
using Ambinder.A2A.Protocol;
using static Ambinder.A2A.Compat.V0_3.Tests.Conversion.ConversionFixtures;

namespace Ambinder.A2A.Compat.V0_3.Tests.Conversion;

public class CardConversionTests
{
    private static AgentCard Card(bool grpc = true, bool extended = true) => new()
    {
        Name = "Hello",
        Description = "Says hello.",
        Version = "1.2.3",
        SupportedInterfaces =
        [
            new AgentInterface { Url = "https://a.example.com/rpc", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" },
            new AgentInterface { Url = "https://a.example.com/rest", ProtocolBinding = "HTTP+JSON", ProtocolVersion = "1.0", Tenant = "t1" },
            .. grpc ? [new AgentInterface { Url = "a.example.com:443", ProtocolBinding = "GRPC", ProtocolVersion = "1.0" }] : Array.Empty<AgentInterface>(),
        ],
        Capabilities = new AgentCapabilities { Streaming = true, ExtendedAgentCard = extended ? true : null },
        SecuritySchemes = new Dictionary<string, SecurityScheme>
        {
            ["bearer"] = new() { HttpAuthSecurityScheme = new HttpAuthSecurityScheme { Scheme = "Bearer" } },
        },
        SecurityRequirements =
        [
            new SecurityRequirement { Schemes = new Dictionary<string, StringList> { ["bearer"] = new() } },
            new SecurityRequirement { Schemes = new Dictionary<string, StringList> { ["oauth"] = new() { List = ["read", "write"] } } },
        ],
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
        Skills = [new AgentSkill { Id = "hi", Name = "Hi", Description = "d", Tags = ["t"] }],
    };

    private static JsonObject Json(AgentCard card) => JsonSerializer.SerializeToNode(card, A2AJson.AgentCard)!.AsObject();

    private static JsonObject Write(AgentCard card, V03CardWriterOptions? options = null)
    {
        JsonObject json = Json(card);
        V03CardWriter.Write(card, json, options ?? new V03CardWriterOptions { IncludeGrpc = true });
        return json;
    }

    private static string Binding(JsonNode? i) => (string)i!["protocolBinding"]!;

    [Fact]
    public void Writer_appends_one_0_3_interface_per_binding_with_the_same_url()
    {
        JsonArray list = Write(Card())["supportedInterfaces"]!.AsArray();

        Assert.Equal(6, list.Count);
        var added = list.Skip(3).ToList();
        Assert.All(added, i => Assert.Equal("0.3", (string)i!["protocolVersion"]!));
        Assert.Equal(["JSONRPC", "HTTP+JSON", "GRPC"], added.Select(Binding));
        Assert.Equal(["https://a.example.com/rpc", "https://a.example.com/rest", "a.example.com:443"], added.Select(i => (string)i!["url"]!));
    }

    [Fact]
    public void Writer_keeps_1_0_interfaces_first_and_unchanged()
    {
        AgentCard card = Card();
        JsonArray original = Json(card)["supportedInterfaces"]!.AsArray();
        JsonArray written = Write(card)["supportedInterfaces"]!.AsArray();

        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i]!.ToJsonString(), written[i]!.ToJsonString());
            Assert.Equal("1.0", (string)written[i]!["protocolVersion"]!);
        }
    }

    [Fact]
    public void Writer_skips_grpc_unless_enabled()
    {
        JsonArray list = Write(Card(), new V03CardWriterOptions())["supportedInterfaces"]!.AsArray();
        Assert.DoesNotContain(list.Skip(3), i => Binding(i) == "GRPC");
        Assert.Equal(5, list.Count);
    }

    [Fact]
    public void Writer_adds_the_legacy_fields_with_LegacyCardFields()
    {
        JsonObject json = Write(Card(), new V03CardWriterOptions { IncludeGrpc = true, AdvertisedVersion = "0.3.0" });

        Assert.Equal("https://a.example.com/rpc", (string)json["url"]!);
        Assert.Equal("JSONRPC", (string)json["preferredTransport"]!);
        Assert.Equal("0.3.0", (string)json["protocolVersion"]!);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""[{"url":"https://a.example.com/rpc","transport":"JSONRPC"},{"url":"https://a.example.com/rest","transport":"HTTP+JSON"},{"url":"a.example.com:443","transport":"GRPC"}]"""),
            json["additionalInterfaces"]));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""[{"bearer":[]},{"oauth":["read","write"]}]"""), json["security"]));
        Assert.True((bool)json["supportsAuthenticatedExtendedCard"]!);
    }

    [Fact]
    public void Writer_honours_AdvertisedVersion()
    {
        Assert.Equal("0.3.2", (string)Write(Card(), new V03CardWriterOptions { AdvertisedVersion = "0.3.2" })["protocolVersion"]!);
    }

    [Fact]
    public void Writer_without_LegacyCardFields_adds_only_interfaces()
    {
        AgentCard card = Card();
        JsonObject before = Json(card);
        JsonObject json = Write(card, new V03CardWriterOptions { LegacyCardFields = false, IncludeGrpc = true });

        Assert.Equal(before.Count, json.Count);
        foreach (string legacy in new[] { "url", "preferredTransport", "additionalInterfaces", "protocolVersion", "security", "supportsAuthenticatedExtendedCard" })
        {
            Assert.False(json.ContainsKey(legacy), legacy);
        }
    }

    [Fact]
    public void Writer_never_changes_an_existing_member()
    {
        AgentCard card = Card();
        JsonObject before = Json(card);
        JsonObject after = Write(card);

        foreach ((string key, JsonNode? value) in before)
        {
            if (key == "supportedInterfaces")
            {
                continue;
            }

            Assert.Equal(value!.ToJsonString(), after[key]!.ToJsonString());
        }

        // A member that is already there (even with a different value) is not overwritten.
        JsonObject preset = Json(card);
        preset["url"] = "https://other.example.com";
        preset["security"] = new JsonArray();
        V03CardWriter.Write(card, preset, new V03CardWriterOptions());
        Assert.Equal("https://other.example.com", (string)preset["url"]!);
        Assert.Empty(preset["security"]!.AsArray());
    }

    [Fact]
    public void Writer_is_idempotent()
    {
        AgentCard card = Card();
        JsonObject json = Write(card);
        string once = json.ToJsonString();
        V03CardWriter.Write(card, json, new V03CardWriterOptions { IncludeGrpc = true });
        Assert.Equal(once, json.ToJsonString());
    }

    [Fact]
    public void Writer_adds_the_members_0_3_requires_when_1_0_omitted_them()
    {
        AgentCard card = new()
        {
            Name = "Bare",
            SupportedInterfaces = [new AgentInterface { Url = "https://b.example.com", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" }],
        };
        JsonObject json = Write(card);
        Assert.Equal("[]", json["skills"]!.ToJsonString());
        Assert.Equal("[]", json["defaultInputModes"]!.ToJsonString());
        Assert.Equal("[]", json["defaultOutputModes"]!.ToJsonString());
        Assert.True(json.ContainsKey("capabilities"));
        Assert.False(json.ContainsKey("security"));
        Assert.False(json.ContainsKey("supportsAuthenticatedExtendedCard"));
    }

    [Fact]
    public void Writer_ignores_unknown_bindings_and_cards_without_mappable_interfaces()
    {
        AgentCard card = new() { Name = "X", SupportedInterfaces = [new AgentInterface { Url = "https://x", ProtocolBinding = "CARRIER-PIGEON", ProtocolVersion = "1.0" }] };
        JsonObject json = Write(card);
        Assert.Single(json["supportedInterfaces"]!.AsArray());
        Assert.False(json.ContainsKey("url"));
    }

    [Fact]
    public void Dual_card_still_parses_strictly_as_1_0_and_equals_the_input_apart_from_the_added_interfaces()
    {
        AgentCard card = Card();
        string text = Write(card).ToJsonString();

        AgentCard parsed = JsonSerializer.Deserialize(text, A2AJson.AgentCard)!;

        Assert.Equal(6, parsed.SupportedInterfaces.Count);
        Assert.Equal(card.SupportedInterfaces, parsed.SupportedInterfaces.Take(3), InterfaceComparer.Instance);
        Assert.All(parsed.SupportedInterfaces.Skip(3), i => Assert.Equal("0.3", i.ProtocolVersion));
        Assert.Equal(V1(card, A2AJson.AgentCard), V1(parsed with { SupportedInterfaces = card.SupportedInterfaces }, A2AJson.AgentCard));
    }

    [Fact]
    public void Dual_card_is_not_handled_by_the_reader()
    {
        using JsonDocument doc = JsonDocument.Parse(Write(Card()).ToJsonString());
        Assert.False(V03CardReader.TryRead(doc.RootElement, out AgentCard? card));
        Assert.Null(card);
    }

    [Fact]
    public void Plain_1_0_card_is_not_handled_by_the_reader()
    {
        using JsonDocument doc = JsonDocument.Parse(Json(Card()).ToJsonString());
        Assert.False(V03CardReader.TryRead(doc.RootElement, out _));
    }

    [Fact]
    public void Reader_converts_the_minimal_card()
    {
        Assert.True(V03CardReader.TryRead(ParseJson(Text("cards/agent-card.minimal.json")), out AgentCard? card));

        AgentInterface only = Assert.Single(card.SupportedInterfaces);
        Assert.Equal(("https://min.example.com/a2a", "JSONRPC", "0.3"), (only.Url, only.ProtocolBinding, only.ProtocolVersion));
        Assert.Equal("Minimal", card.Name);
        Assert.Equal("1.0.0", card.Version);
        Assert.Equal(["text/plain"], card.DefaultInputModes);
        Assert.Empty(card.Skills);
        Assert.Null(card.Capabilities.ExtendedAgentCard);
    }

    [Fact]
    public void Reader_builds_interfaces_from_url_preferredTransport_and_additionalInterfaces_without_duplicates()
    {
        Assert.True(V03CardReader.TryRead(ParseJson(Text("cards/agent-card.additional-interfaces-security.json")), out AgentCard? card));

        Assert.Equal(
            [("https://edge.example.com/a2a/jsonrpc", "JSONRPC"), ("https://edge.example.com/a2a/grpc", "GRPC"), ("https://edge.example.com/a2a/rest", "HTTP+JSON")],
            card.SupportedInterfaces.Select(i => (i.Url, i.ProtocolBinding)));
        Assert.All(card.SupportedInterfaces, i => Assert.Equal("0.3", i.ProtocolVersion));
    }

    [Fact]
    public void Reader_maps_the_other_fields_by_name()
    {
        Assert.True(V03CardReader.TryRead(ParseJson(Text("cards/agent-card.additional-interfaces-security.json")), out AgentCard? card));

        Assert.Equal("Edge Case Agent", card.Name);
        Assert.Equal("2.1.0", card.Version);
        Assert.Equal("https://docs.example.com/edge", card.DocumentationUrl);
        Assert.Equal("https://edge.example.com/icon.png", card.IconUrl);
        Assert.Equal(("Example Org", "https://example.com"), (card.Provider!.Organization, card.Provider.Url));
        Assert.True(card.Capabilities.Streaming);
        Assert.True(card.Capabilities.PushNotifications);
        Assert.True(card.Capabilities.ExtendedAgentCard);
        AgentExtension ext = Assert.Single(card.Capabilities.Extensions);
        Assert.Equal("https://example.com/ext/demo/v1", ext.Uri);
        Assert.Equal(2, ext.Params!.Value.GetProperty("level").GetInt32());
        Assert.Equal(["text/plain", "application/json"], card.DefaultInputModes);
        AgentSkill skill = Assert.Single(card.Skills);
        Assert.Equal(("echo", "Echo", "Echoes input."), (skill.Id, skill.Name, skill.Description));
        Assert.Equal(["echo", "demo"], skill.Tags);
        Assert.Equal(["say hi"], skill.Examples);
        Assert.Equal(["text/plain"], skill.InputModes);
        Assert.Equal(["bearerAuth"], Assert.Single(skill.SecurityRequirements).Schemes.Keys);
        AgentCardSignature sig = Assert.Single(card.Signatures);
        Assert.Equal(("eyJhbGciOiJFUzI1NiJ9", "c2lnbmF0dXJl"), (sig.Protected, sig.Signature));
        Assert.Equal("key-1", sig.Header!.Value.GetProperty("kid").GetString());
    }

    [Fact]
    public void Reader_maps_security_to_securityRequirements()
    {
        Assert.True(V03CardReader.TryRead(ParseJson(Text("cards/agent-card.additional-interfaces-security.json")), out AgentCard? card));

        Assert.Equal(2, card.SecurityRequirements.Count);
        Assert.Equal(["bearerAuth"], card.SecurityRequirements[0].Schemes.Keys);
        Assert.Empty(card.SecurityRequirements[0].Schemes["bearerAuth"].List);
        Assert.Equal(["read", "write"], card.SecurityRequirements[1].Schemes["oauth"].List);
        Assert.Empty(card.SecurityRequirements[1].Schemes["apiKeyAuth"].List);
    }

    [Fact]
    public void Reader_maps_security_schemes_to_the_1_0_wrappers()
    {
        Assert.True(V03CardReader.TryRead(ParseJson(Text("cards/agent-card.additional-interfaces-security.json")), out AgentCard? card));
        var schemes = card.SecuritySchemes!;

        Assert.Equal(("header", "X-API-Key", "API key"), (schemes["apiKeyAuth"].ApiKeySecurityScheme!.Location, schemes["apiKeyAuth"].ApiKeySecurityScheme!.Name, schemes["apiKeyAuth"].ApiKeySecurityScheme!.Description));
        Assert.Equal(("bearer", "JWT"), (schemes["bearerAuth"].HttpAuthSecurityScheme!.Scheme, schemes["bearerAuth"].HttpAuthSecurityScheme!.BearerFormat));
        var oauth = schemes["oauth"].Oauth2SecurityScheme!;
        Assert.Equal("https://auth.example.com/.well-known/oauth-authorization-server", oauth.Oauth2MetadataUrl);
        Assert.Equal("https://auth.example.com/token", oauth.Flows.AuthorizationCode!.TokenUrl);
        Assert.Equal("Read access", oauth.Flows.AuthorizationCode.Scopes["read"]);
        Assert.Null(oauth.Flows.ClientCredentials); // oneof: only the first flow is kept
        Assert.Equal("https://auth.example.com/.well-known/openid-configuration", schemes["oidc"].OpenIdConnectSecurityScheme!.OpenIdConnectUrl);
        Assert.Equal("Client certificate", schemes["mtls"].MtlsSecurityScheme!.Description);
    }

    [Theory]
    [MemberData(nameof(CardFixtures))]
    public void Reader_output_serializes_and_parses_as_a_1_0_card(string rel)
    {
        Assert.True(V03CardReader.TryRead(ParseJson(Text(rel)), out AgentCard? card));
        AgentCard roundTripped = JsonSerializer.Deserialize(JsonSerializer.Serialize(card, A2AJson.AgentCard), A2AJson.AgentCard)!;
        Assert.Equal(V1(card, A2AJson.AgentCard), V1(roundTripped, A2AJson.AgentCard));
        Assert.All(card.SupportedInterfaces, i => Assert.Equal("0.3", i.ProtocolVersion));
    }

    [Fact]
    public void Reader_defaults_an_empty_preferredTransport_to_JSONRPC_and_accepts_0_2_x()
    {
        Assert.True(V03CardReader.TryRead(ParseJson(Text("cards/agent-card.sample.json")), out AgentCard? card)); // protocolVersion 0.2.9
        Assert.Equal("JSONRPC", card.SupportedInterfaces[0].ProtocolBinding);
        Assert.Equal(3, card.SupportedInterfaces.Count);
    }

    [Fact]
    public void Reader_returns_false_for_a_card_without_url_or_a_non_object()
    {
        Assert.False(V03CardReader.TryRead(ParseJson("""{"name":"x"}"""), out _));
        Assert.False(V03CardReader.TryRead(ParseJson("[]"), out _));
    }

    [Fact]
    public void Writer_then_reader_round_trip_of_a_0_3_view_gives_back_the_0_3_interfaces()
    {
        // A client that only understands 0.3 sees url/preferredTransport/additionalInterfaces; strip the 1.0 members to simulate it.
        JsonObject json = Write(Card());
        json.Remove("supportedInterfaces");
        using JsonDocument doc = JsonDocument.Parse(json.ToJsonString());
        Assert.True(V03CardReader.TryRead(doc.RootElement, out AgentCard? card));
        Assert.Equal(
            [("https://a.example.com/rpc", "JSONRPC"), ("https://a.example.com/rest", "HTTP+JSON"), ("a.example.com:443", "GRPC")],
            card.SupportedInterfaces.Select(i => (i.Url, i.ProtocolBinding)));
    }

    public static TheoryData<string> CardFixtures() => Data("cards");

    private sealed class InterfaceComparer : IEqualityComparer<AgentInterface>
    {
        public static readonly InterfaceComparer Instance = new();
        public bool Equals(AgentInterface? x, AgentInterface? y) => x == y;
        public int GetHashCode(AgentInterface obj) => obj.GetHashCode();
    }
}
