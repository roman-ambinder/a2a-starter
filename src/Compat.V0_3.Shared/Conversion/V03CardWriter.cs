using System.Text.Json.Nodes;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Compat.V0_3.Shared.Conversion;

/// <summary>What <see cref="V03CardWriter"/> writes. The public <c>V0_3ServerOptions</c> (Task 8) maps onto this.</summary>
internal sealed record V03CardWriterOptions
{
    /// <summary>Also write the 0.3 top-level members (<c>url</c>, <c>preferredTransport</c>, ...). Default <see langword="true"/>.</summary>
    public bool LegacyCardFields { get; init; } = true;

    /// <summary>The value of the legacy <c>protocolVersion</c> member. Default <c>0.3.0</c>.</summary>
    public string AdvertisedVersion { get; init; } = "0.3.0";

    /// <summary>
    /// Also add a 0.3 interface for each <c>GRPC</c> interface. Default <see langword="false"/>, because the 0.3 gRPC service
    /// (<c>MapA2AGrpcV0_3</c>) is opt-in; the contributor (Task 12) sets it when that service is mapped.
    /// </summary>
    public bool IncludeGrpc { get; init; }
}

/// <summary>
/// Adds the 0.3 view of a 1.0 agent card to the card JSON: one 0.3 interface per binding and, with
/// <see cref="V03CardWriterOptions.LegacyCardFields"/>, the 0.3 top-level members. Add-only: it never changes or removes a
/// member that is already in the JSON, and running it twice gives the same JSON. See docs/spec-decisions.md, "Cards".
/// </summary>
internal static class V03CardWriter
{
    /// <summary>The <c>protocolVersion</c> of the interfaces this writer adds (Major.Minor only; see the spec-decisions "Cards" rows).</summary>
    public const string InterfaceVersion = "0.3";

    /// <summary>Adds the 0.3 interfaces and legacy members for <paramref name="card"/> to <paramref name="cardJson"/> (the serialized 1.0 card).</summary>
    public static void Write(AgentCard card, JsonObject cardJson, V03CardWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(cardJson);
        options ??= new V03CardWriterOptions();

        List<AgentInterface> v03 = SelectInterfaces(card, options);
        AppendInterfaces(cardJson, v03);

        if (!options.LegacyCardFields || v03.Count == 0)
        {
            return;
        }

        // The main interface is the 0.3 JSON-RPC one, else the first 0.3 interface (design: "Server: the agent card").
        AgentInterface main = v03.Find(i => i.ProtocolBinding == A2AConstants.BindingJsonRpc) ?? v03[0];
        AddIfAbsent(cardJson, "protocolVersion", options.AdvertisedVersion);
        AddIfAbsent(cardJson, "url", main.Url);
        AddIfAbsent(cardJson, "preferredTransport", main.ProtocolBinding);
        if (!cardJson.ContainsKey("additionalInterfaces"))
        {
            // 0.3 says the list SHOULD include the main url/transport too (spec 5.6); the vendored text wins over plan 15's "the other interfaces".
            JsonArray additional = [];
            foreach (AgentInterface i in v03)
            {
                JsonNode item = new JsonObject { ["url"] = i.Url, ["transport"] = i.ProtocolBinding };
                additional.Add(item);
            }

            cardJson["additionalInterfaces"] = additional;
        }

        if (card.SecurityRequirements.Count > 0 && !cardJson.ContainsKey("security"))
        {
            cardJson["security"] = ToSecurity(card.SecurityRequirements);
        }

        if (card.Capabilities.ExtendedAgentCard is { } extended)
        {
            AddIfAbsent(cardJson, "supportsAuthenticatedExtendedCard", extended);
        }

        // 1.0 omits empty lists and an empty capabilities object, but 0.3 requires these members.
        AddEmptyIfAbsent(cardJson, "capabilities", () => new JsonObject());
        AddEmptyIfAbsent(cardJson, "defaultInputModes", () => new JsonArray());
        AddEmptyIfAbsent(cardJson, "defaultOutputModes", () => new JsonArray());
        AddEmptyIfAbsent(cardJson, "skills", () => new JsonArray());
    }

    /// <summary>The 0.3 interfaces for a card, deduplicated by binding and URL, in card order.</summary>
    private static List<AgentInterface> SelectInterfaces(AgentCard card, V03CardWriterOptions options)
    {
        List<AgentInterface> result = [];
        foreach (AgentInterface i in card.SupportedInterfaces)
        {
            bool mapped = i.ProtocolBinding switch
            {
                A2AConstants.BindingJsonRpc or A2AConstants.BindingRest => true,
                A2AConstants.BindingGrpc => options.IncludeGrpc,
                _ => false,
            };
            string version = MajorMinor(i.ProtocolVersion);
            if (!mapped || (version != InterfaceVersion && version != A2AConstants.ProtocolVersion) || i.Url.Length == 0)
            {
                continue;
            }

            if (!result.Exists(r => r.ProtocolBinding == i.ProtocolBinding && r.Url == i.Url))
            {
                result.Add(new AgentInterface { Url = i.Url, ProtocolBinding = i.ProtocolBinding, Tenant = i.Tenant, ProtocolVersion = InterfaceVersion });
            }
        }

        return result;
    }

    private static void AppendInterfaces(JsonObject cardJson, List<AgentInterface> v03)
    {
        if (v03.Count == 0)
        {
            return;
        }

        if (cardJson["supportedInterfaces"] is not JsonArray array)
        {
            if (cardJson.ContainsKey("supportedInterfaces"))
            {
                return; // not an array: not ours to change
            }

            array = [];
            cardJson["supportedInterfaces"] = array;
        }

        foreach (AgentInterface i in v03)
        {
            if (array.OfType<JsonObject>().Any(e => IsSame(e, i)))
            {
                continue;
            }

            JsonObject entry = new() { ["url"] = i.Url, ["protocolBinding"] = i.ProtocolBinding };
            if (i.Tenant is { Length: > 0 } tenant)
            {
                entry["tenant"] = tenant;
            }

            entry["protocolVersion"] = InterfaceVersion;
            array.Add((JsonNode)entry);
        }
    }

    private static bool IsSame(JsonObject entry, AgentInterface i) =>
        entry["url"]?.GetValueKind() == System.Text.Json.JsonValueKind.String && (string?)entry["url"] == i.Url
        && (string?)entry["protocolBinding"] == i.ProtocolBinding
        && MajorMinor((string?)entry["protocolVersion"]) == InterfaceVersion;

    private static JsonArray ToSecurity(IReadOnlyList<SecurityRequirement> requirements)
    {
        JsonArray result = [];
        foreach (SecurityRequirement requirement in requirements)
        {
            JsonObject map = [];
            foreach ((string scheme, StringList scopes) in requirement.Schemes)
            {
                JsonArray list = [];
                foreach (string scope in scopes.List)
                {
                    list.Add((JsonNode?)JsonValue.Create(scope));
                }

                map[scheme] = list;
            }

            result.Add((JsonNode)map);
        }

        return result;
    }

    private static void AddIfAbsent(JsonObject json, string name, string value)
    {
        if (!json.ContainsKey(name))
        {
            json[name] = value;
        }
    }

    private static void AddIfAbsent(JsonObject json, string name, bool value)
    {
        if (!json.ContainsKey(name))
        {
            json[name] = value;
        }
    }

    private static void AddEmptyIfAbsent(JsonObject json, string name, Func<JsonNode> create)
    {
        if (!json.ContainsKey(name))
        {
            json[name] = create();
        }
    }

    /// <summary>Major.Minor of a version string; empty or null gives <c>1.0</c> (the core's rule for an interface without a version).</summary>
    internal static string MajorMinor(string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return A2AConstants.ProtocolVersion;
        }

        int first = version.IndexOf('.', StringComparison.Ordinal);
        if (first < 0)
        {
            return version + ".0";
        }

        int second = version.IndexOf('.', first + 1);
        return second < 0 ? version : version[..second];
    }
}
