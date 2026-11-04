using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Ambinder.A2A.Compat.V0_3.Shared.Json;
using Ambinder.A2A.Compat.V0_3.Shared.Models;

namespace Ambinder.A2A.Compat.V0_3.Tests.Json;

/// <summary>
/// Checks every file under <c>tests/vectors/v0_3</c> (the Task 5 fixtures; provenance is in that folder's README.md).
/// All fixtures must be valid JSON. Fixtures under cards, events, responses and push, and the <c>params</c> of the
/// message/send and message/stream requests, must deserialize with <see cref="V0_3JsonContext"/> and write back
/// structurally equal. The writer adds the schema-required <c>kind</c> to objects the example left it out of (the spec's own
/// history messages have none); that is the only allowed difference.
/// The <c>*.spec-verbatim.json</c> fixtures are spec examples that show a file part as {"mimeType","data"} where a2a.json
/// has <c>bytes</c>. They are kept as published and must throw <see cref="JsonException"/> (current behavior, documented in the README).
/// </summary>
public class FixtureParseTests
{
    private static readonly V0_3JsonContext Ctx = V0_3JsonContext.Default;

    private static readonly string VectorsRoot = FindVectorsRoot();

    private static string FindVectorsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("*.slnx").Length == 0)
        {
            dir = dir.Parent;
        }

        return Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("No .slnx found above " + AppContext.BaseDirectory),
            "tests", "vectors", "v0_3");
    }

    private static IEnumerable<string> AllFiles() =>
        Directory.EnumerateFiles(VectorsRoot, "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(VectorsRoot, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal);

    public static TheoryData<string> All() => new(AllFiles());

    public static TheoryData<string> Typed() => new(AllFiles().Where(IsTyped));

    public static TheoryData<string> Verbatim() => new(AllFiles().Where(f => f.EndsWith(".spec-verbatim.json", StringComparison.Ordinal) && f != MisplacedMessageId));

    /// <summary>Spec 9.4 puts messageId in params, not in message. Documented in the README; the reader does not throw.</summary>
    private const string MisplacedMessageId = "requests/message-send.multi-turn.request-1.spec-verbatim.json";

    public static TheoryData<string> Errors() => new(AllFiles().Where(f => f.StartsWith("errors/", StringComparison.Ordinal)));

    private static bool IsTyped(string rel) =>
        !rel.EndsWith(".spec-verbatim.json", StringComparison.Ordinal)
        && (rel.StartsWith("cards/", StringComparison.Ordinal) || rel.StartsWith("events/", StringComparison.Ordinal)
            || rel.StartsWith("responses/", StringComparison.Ordinal) || rel.StartsWith("push/", StringComparison.Ordinal)
            || IsMessageRequest(rel));

    private static bool IsMessageRequest(string rel)
    {
        if (!rel.StartsWith("requests/", StringComparison.Ordinal))
        {
            return false;
        }

        string method = (string?)Load(rel)?["method"] ?? "";
        return method is "message/send" or "message/stream";
    }

    private static string Text(string rel) => File.ReadAllText(Path.Combine(VectorsRoot, rel));

    private static JsonNode? Load(string rel) => JsonNode.Parse(Text(rel));

    /// <summary>The value the model reads: the card, the push Task, the request params, or the JSON-RPC result.</summary>
    private static JsonNode Subject(string rel)
    {
        JsonNode root = Load(rel)!;
        if (root is JsonObject o)
        {
            if (o.TryGetPropertyValue("params", out var p) && p is not null)
            {
                return p;
            }

            if (o.TryGetPropertyValue("result", out var r) && r is not null)
            {
                return r;
            }
        }

        return root;
    }

    private static string Kind(JsonNode n) => (string?)n["kind"] ?? "";

    private static string RoundTripText(string rel, string json)
    {
        if (rel.StartsWith("cards/", StringComparison.Ordinal))
        {
            return Write(json, Ctx.V03AgentCard);
        }

        if (rel.StartsWith("requests/", StringComparison.Ordinal))
        {
            return Write(json, Ctx.V03MessageSendParams);
        }

        using var doc = JsonDocument.Parse(json);
        // The spec's own 9.5 submitted-task result has no "kind"; a result with a status is a Task.
        string kind = doc.RootElement.TryGetProperty("kind", out var k) ? k.GetString()! : "task";
        return kind switch
        {
            "task" => Write(json, Ctx.V03Task),
            "message" => Write(json, Ctx.V03Message),
            "status-update" => Write(json, Ctx.V03TaskStatusUpdateEvent),
            "artifact-update" => Write(json, Ctx.V03TaskArtifactUpdateEvent),
            _ => throw new InvalidOperationException($"{rel}: no model for kind '{kind}'"),
        };
    }

    private static string Write<T>(string json, JsonTypeInfo<T> info) =>
        JsonSerializer.Serialize(JsonSerializer.Deserialize(json, info)!, info);

    [Theory]
    [MemberData(nameof(All))]
    public void Every_fixture_is_valid_json(string rel)
    {
        using var doc = JsonDocument.Parse(Text(rel));
        Assert.NotEqual(JsonValueKind.Undefined, doc.RootElement.ValueKind);
    }

    [Fact]
    public void Fixture_set_is_not_empty_and_covers_every_folder()
    {
        var files = AllFiles().ToList();
        foreach (string folder in new[] { "requests", "responses", "events", "cards", "push", "errors" })
        {
            Assert.Contains(files, f => f.StartsWith(folder + "/", StringComparison.Ordinal));
        }
    }

    [Theory]
    [MemberData(nameof(Typed))]
    public void Fixture_deserializes_and_round_trips_structurally_equal(string rel)
    {
        string subject = Subject(rel).ToJsonString();
        string written = RoundTripText(rel, subject);
        AssertSame(JsonNode.Parse(subject), JsonNode.Parse(written), rel + ": $");
    }

    [Theory]
    [MemberData(nameof(Verbatim))]
    public void Spec_verbatim_fixture_is_rejected_with_JsonException(string rel)
    {
        string subject = Subject(rel).ToJsonString();
        Assert.Throws<JsonException>(() => RoundTripText(rel, subject));
    }

    [Fact]
    public void Spec_verbatim_misplaced_message_id_is_dropped_and_leaves_the_id_missing()
    {
        string subject = Subject(MisplacedMessageId).ToJsonString();
        var value = JsonSerializer.Deserialize(subject, Ctx.V03MessageSendParams)!;
        Assert.True(string.IsNullOrEmpty(value.Message.MessageId));
    }

    [Fact]
    public void Verbatim_fixtures_all_have_a_corrected_sibling()
    {
        foreach (string rel in AllFiles().Where(f => f.EndsWith(".spec-verbatim.json", StringComparison.Ordinal)))
        {
            string sibling = rel.Replace(".spec-verbatim", "", StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(VectorsRoot, sibling)), $"{rel} has no corrected sibling {sibling}");
        }
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public void Error_fixture_parses_into_its_model(string rel)
    {
        JsonNode root = Load(rel)!;
        if (root["error"] is JsonObject error)
        {
            Assert.Equal("2.0", (string?)root["jsonrpc"]);
            Assert.True(((JsonObject)root).ContainsKey("id"), $"{rel}: JSON-RPC error envelope needs an id");
            string json = error.ToJsonString();
            var value = JsonSerializer.Deserialize(json, Ctx.V03JsonRpcError)!;
            Assert.Equal((int)error["code"]!, value.Code);
            AssertSame(error, JsonNode.Parse(JsonSerializer.Serialize(value, Ctx.V03JsonRpcError)), rel + ": $.error");
        }
        else
        {
            var value = JsonSerializer.Deserialize(root.ToJsonString(), Ctx.V03ProblemDetails)!;
            AssertSame(root, JsonNode.Parse(JsonSerializer.Serialize(value, Ctx.V03ProblemDetails)), rel + ": $");
        }
    }

    [Fact]
    public void Every_task_state_in_the_inventory_has_a_task_fixture()
    {
        using var inv = JsonDocument.Parse(File.ReadAllText(Path.Combine(VectorsRoot, "inventory.json")));
        var states = inv.RootElement.GetProperty("taskStates").GetProperty("jsonEnumOrder").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(9, states.Count);
        var seen = AllFiles().Where(f => f.StartsWith("responses/", StringComparison.Ordinal))
            .Select(f => (string?)Subject(f)["status"]?["state"]).ToHashSet();
        foreach (string s in states)
        {
            Assert.Contains(s, seen);
        }
    }

    [Fact]
    public void Every_error_code_in_the_inventory_has_a_jsonrpc_error_fixture()
    {
        using var inv = JsonDocument.Parse(File.ReadAllText(Path.Combine(VectorsRoot, "inventory.json")));
        var codes = inv.RootElement.GetProperty("errorCodes").GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetInt32()).ToList();
        Assert.Equal(12, codes.Count);
        var seen = AllFiles().Where(f => f.StartsWith("errors/", StringComparison.Ordinal))
            .Select(f => (int?)Load(f)!["error"]?["code"]).ToHashSet();
        foreach (int c in codes)
        {
            Assert.Contains(c, seen);
        }
    }

    [Fact]
    public void Status_updates_cover_final_true_and_false_and_artifact_updates_cover_append_and_last_chunk()
    {
        var events = AllFiles().Where(f => f.StartsWith("events/", StringComparison.Ordinal)).Select(Subject).ToList();
        var status = events.Where(e => Kind(e) == "status-update").Select(e => (bool)e["final"]!).ToHashSet();
        Assert.Equal(new HashSet<bool> { true, false }, status);
        Assert.Contains(events, e => Kind(e) == "artifact-update" && (bool?)e["append"] == true && (bool?)e["lastChunk"] == true);
    }

    [Fact]
    public void Every_fixture_is_listed_in_the_readme_provenance_table()
    {
        string readme = File.ReadAllText(Path.Combine(VectorsRoot, "README.md"));
        foreach (string rel in AllFiles().Where(f => f != "inventory.json" && !f.StartsWith("captured/", StringComparison.Ordinal)))
        {
            Assert.Contains($"`{rel}`", readme);
        }
    }

    /// <summary>Structural equality. Object member order is ignored; actual may add a <c>kind</c> member the example lacks.</summary>
    private static void AssertSame(JsonNode? expected, JsonNode? actual, string path)
    {
        switch (expected)
        {
            case JsonObject eo:
                var ao = Assert.IsType<JsonObject>(actual);
                foreach (var (key, value) in eo)
                {
                    Assert.True(ao.ContainsKey(key), $"{path}.{key} is missing after the round trip");
                    AssertSame(value, ao[key], $"{path}.{key}");
                }

                foreach (var (key, _) in ao)
                {
                    Assert.True(eo.ContainsKey(key) || key == "kind", $"{path}.{key} was added by the round trip");
                }

                break;
            case JsonArray ea:
                var aa = Assert.IsType<JsonArray>(actual);
                Assert.True(ea.Count == aa.Count, $"{path} has {aa.Count} items, expected {ea.Count}");
                for (int i = 0; i < ea.Count; i++)
                {
                    AssertSame(ea[i], aa[i], $"{path}[{i}]");
                }

                break;
            case null:
                Assert.True(actual is null, $"{path}: expected null");
                break;
            default:
                Assert.True(actual is JsonValue, $"{path}: expected a value");
                Assert.True(JsonNode.DeepEquals(expected, actual), $"{path}: expected {expected.ToJsonString()}, got {actual?.ToJsonString()}");
                break;
        }
    }
}
