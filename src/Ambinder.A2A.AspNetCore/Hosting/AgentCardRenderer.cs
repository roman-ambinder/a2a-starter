using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Shared;

namespace Ambinder.A2A.AspNetCore.Hosting;

/// <summary>
/// The served form of a public agent card: its JSON after every <see cref="IAgentCardContributor"/> ran, cached once per card instance
/// (cards are immutable records built at startup). One per host, so contributors of one host never shape another's cards.
/// </summary>
internal sealed class AgentCardRenderer(IEnumerable<IAgentCardContributor> contributors)
{
    private readonly IAgentCardContributor[] _contributors = contributors.ToArray();
    private readonly ConditionalWeakTable<AgentCard, RenderedCard> _cards = new();

    /// <summary>The served bytes and ETag of the public card <paramref name="card"/>.</summary>
    public RenderedCard Render(string agentName, AgentCard card)
        => _cards.GetValue(card, c => Build(new AgentCardContext { AgentName = agentName, Card = c }));

    private RenderedCard Build(AgentCardContext context)
    {
        var json = JsonSerializer.SerializeToNode(context.Card, A2AJson.Options.GetTypeInfo(typeof(AgentCard)))!.AsObject();
        foreach (var contributor in _contributors)
        {
#if DEBUG
            var before = (JsonObject)json.DeepClone();
#endif
            contributor.Contribute(context, json);
#if DEBUG
            var violation = AgentCardContributionRules.FindViolation(before, json);
            Debug.Assert(violation is null, $"{contributor.GetType().FullName} broke the IAgentCardContributor rules: {violation}");
#endif
        }
        var bytes = ToBytes(json);

        // B13: hash the card without its signatures, plus the signing key ids, so re-signing with the same keys keeps the ETag.
        // Package 04 replaces this payload with the RFC 8785 canonical form; the hash rule stays the same.
        var keyIds = KeyIds(json);
        var payload = json.Remove("signatures") ? ToBytes(json) : bytes;
        return new RenderedCard(bytes, AgentCardETag.Compute(payload, keyIds));
    }

    private static List<string> KeyIds(JsonObject json)
    {
        var kids = new List<string>();
        if (json["signatures"] is not JsonArray signatures) return kids;
        foreach (var signature in signatures)
        {
            if (signature is not JsonObject entry) continue;
            var kid = KeyIdOf(entry["protected"]) ?? (entry["header"] as JsonObject)?["kid"]?.GetValue<string>();
            if (kid is not null) kids.Add(kid);
        }
        return kids;
    }

    private static string? KeyIdOf(JsonNode? protectedHeader)
    {
        if (protectedHeader is not JsonValue value || !value.TryGetValue<string>(out var text) || text.Length == 0) return null;
        try
        {
            return JsonNode.Parse(Base64Url.DecodeFromChars(text)) is JsonObject header ? header["kid"]?.GetValue<string>() : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] ToBytes(JsonObject json)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = A2AJson.Options.Encoder }))
            json.WriteTo(writer, A2AJson.Options);
        return buffer.WrittenSpan.ToArray();
    }
}

/// <summary>A served card.</summary>
/// <param name="Bytes">The card JSON as served.</param>
/// <param name="ETag">Strong ETag from <c>AgentCardETag.Compute</c>: the same on every instance, and unchanged when only a signature value changes.</param>
internal sealed record RenderedCard(byte[] Bytes, string ETag);

/// <summary>What an <see cref="IAgentCardContributor"/> may change (design D7): it may add, never change or remove.</summary>
internal static class AgentCardContributionRules
{
    /// <summary>Null when <paramref name="after"/> only adds to <paramref name="before"/> as D7 allows; otherwise what broke the rules.</summary>
    public static string? FindViolation(JsonObject before, JsonObject after)
        => CheckObject("", before, after, allowNewKey: _ => true, member: name => name switch
        {
            "supportedInterfaces" => AppendOnly,
            "capabilities" => (path, old, now) => CheckObject(path, old, now, allowNewKey: k => k == "extensions",
                member: k => k == "extensions" ? AppendOnly : Unchanged),
            "securitySchemes" => (path, old, now) => CheckObject(path, old, now, allowNewKey: _ => true, member: _ => Unchanged),
            "securityRequirements" => SetWhenNone,
            "skills" => Skills,
            _ => Unchanged,
        });

    private delegate string? Check(string path, JsonNode? old, JsonNode? now);

    private static string? CheckObject(string path, JsonNode? old, JsonNode? now, Func<string, bool> allowNewKey, Func<string, Check> member)
    {
        if (old is not JsonObject oldObject || now is not JsonObject newObject) return Unchanged(path, old, now);
        foreach (var (key, oldValue) in oldObject)
        {
            var at = path.Length == 0 ? key : $"{path}.{key}";
            if (!newObject.TryGetPropertyValue(key, out var newValue)) return $"'{at}' was removed";
            if (member(key)(at, oldValue, newValue) is { } violation) return violation;
        }
        foreach (var (key, _) in newObject)
        {
            if (!oldObject.ContainsKey(key) && !allowNewKey(key)) return $"'{(path.Length == 0 ? key : $"{path}.{key}")}' was added";
        }
        return null;
    }

    private static string? Unchanged(string path, JsonNode? old, JsonNode? now)
        => JsonNode.DeepEquals(old, now) ? null : $"'{path}' was changed";

    private static string? AppendOnly(string path, JsonNode? old, JsonNode? now)
    {
        if (old is not JsonArray oldArray || now is not JsonArray newArray || newArray.Count < oldArray.Count)
            return Unchanged(path, old, now);
        for (var i = 0; i < oldArray.Count; i++)
            if (!JsonNode.DeepEquals(oldArray[i], newArray[i])) return $"'{path}[{i}]' was changed";
        return null;
    }

    private static string? SetWhenNone(string path, JsonNode? old, JsonNode? now)
        => old is null or JsonArray { Count: 0 } ? null : Unchanged(path, old, now);

    private static string? Skills(string path, JsonNode? old, JsonNode? now)
    {
        if (old is not JsonArray oldArray || now is not JsonArray newArray || newArray.Count != oldArray.Count)
            return Unchanged(path, old, now);
        for (var i = 0; i < oldArray.Count; i++)
        {
            var violation = CheckObject($"{path}[{i}]", oldArray[i], newArray[i], allowNewKey: k => k == "securityRequirements",
                member: k => k == "securityRequirements" ? SetWhenNone : Unchanged);
            if (violation is not null) return violation;
        }
        return null;
    }
}
