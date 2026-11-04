using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Ambinder.A2A.Compat.V0_3.Shared.Json;
using Ambinder.A2A.Compat.V0_3.Shared.Models;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Compat.V0_3.Tests.Conversion;

/// <summary>Loads the Task 5 fixtures (tests/vectors/v0_3) for the converter tests and compares models as JSON.</summary>
internal static class ConversionFixtures
{
    public static readonly V0_3JsonContext Ctx = V0_3JsonContext.Default;

    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("*.slnx").Length == 0)
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("No .slnx above " + AppContext.BaseDirectory), "tests", "vectors", "v0_3");
    }

    /// <summary>Fixtures of a folder, without the <c>*.spec-verbatim.json</c> ones (those are not valid 0.3 and are covered by FixtureParseTests).</summary>
    public static IEnumerable<string> Files(string folder) =>
        Directory.EnumerateFiles(Path.Combine(Root, folder), "*.json")
            .Where(f => !f.EndsWith(".spec-verbatim.json", StringComparison.Ordinal))
            .Select(f => folder + "/" + Path.GetFileName(f))
            .OrderBy(f => f, StringComparer.Ordinal);

    public static TheoryData<string> Data(string folder, Func<string, bool>? filter = null) => new(Files(folder).Where(f => filter?.Invoke(f) ?? true));

    public static string Text(string rel) => File.ReadAllText(Path.Combine(Root, rel));

    /// <summary>The <c>result</c> (or <c>params</c> or <c>error</c>) member of an envelope fixture, as JSON text.</summary>
    public static string Member(string rel, string member) => JsonNode.Parse(Text(rel))![member]!.ToJsonString();

    public static IV03Result Result(string rel) => JsonSerializer.Deserialize(Member(rel, "result"), Ctx.IV03Result)!;

    public static IV03StreamEvent Event(string rel) => JsonSerializer.Deserialize(Member(rel, "result"), Ctx.IV03StreamEvent)!;

    public static V03MessageSendParams SendParams(string rel) => JsonSerializer.Deserialize(Member(rel, "params"), Ctx.V03MessageSendParams)!;

    public static V03Task PushTask(string rel) => JsonSerializer.Deserialize(Text(rel), Ctx.V03Task)!;

    public static bool IsMessageRequest(string rel) => JsonNode.Parse(Text(rel))!["method"]!.GetValue<string>() is "message/send" or "message/stream";

    public static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static IReadOnlyDictionary<string, JsonElement> Obj(string json) =>
        ParseJson(json).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    /// <summary>A 1.0 value as JSON text; two 1.0 values are equal when this is equal (records compare lists by reference).</summary>
    public static string V1<T>(T value, JsonTypeInfo<T> info) => JsonSerializer.Serialize(value, info);

    /// <summary>
    /// A 0.3 value as normalized JSON. The 0.3 writer and the 1.0 models differ from a fixture in ways that carry no information:
    /// empty arrays are omitted, <c>append</c>/<c>lastChunk</c> of <c>false</c> equal absent, a timestamp is the same instant in UTC
    /// (a fixture without an offset counts as UTC), an empty <c>messageId</c> equals an absent one (the spec's own examples omit it and 1.0 reads
    /// that as <c>""</c>), and <c>final</c> is set by the stream writer, not the converter (Task 10), so it is removed.
    /// </summary>
    public static JsonNode Normalized<T>(T value, JsonTypeInfo<T> info) => Normalize(JsonSerializer.SerializeToNode(value, info))!;

    private static JsonNode? Normalize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj.ToList())
                {
                    if (key == "final"
                        || (key == "messageId" && child is JsonValue { } id && id.TryGetValue<string>(out var idText) && idText.Length == 0)
                        || (key is "append" or "lastChunk" && child is JsonValue v && v.TryGetValue<bool>(out var b) && !b)
                        || (child is JsonArray { Count: 0 }))
                    {
                        obj.Remove(key);
                    }
                    else if (key == "timestamp" && child is JsonValue t && t.TryGetValue<string>(out var s))
                    {
                        obj[key] = V03TaskTimestamp(s);
                    }
                    else
                    {
                        Normalize(child);
                    }
                }

                return obj;
            case JsonArray arr:
                for (int i = 0; i < arr.Count; i++)
                {
                    Normalize(arr[i]);
                }

                return arr;
            default:
                return node;
        }
    }

    private static string V03TaskTimestamp(string s) =>
        DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal)
            .UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
