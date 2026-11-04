using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ambinder.A2A.Tests.WireFixtures;

public static class FixtureCatalog
{
    /// <summary>Copied next to the test assembly by the csproj (see Step 3b).</summary>
    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "vectors", "wire", "v1.0");

    static readonly JsonSerializerOptions MetaOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static IReadOnlyList<WireFixture> Load(string root)
    {
        var list = new List<WireFixture>();
        foreach (var json in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                                      .Where(p => !p.EndsWith(".meta.json", StringComparison.Ordinal))
                                      .Order(StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(root, json).Replace('\\', '/');
            var id = rel[..^".json".Length];
            var meta = JsonSerializer.Deserialize<FixtureMeta>(File.ReadAllText(MetaPath(json)), MetaOptions)
                       ?? throw new InvalidDataException($"{id}: empty meta");
            list.Add(new WireFixture(id, id.Split('/')[0], json, meta));
        }
        return list;
    }

    public static IReadOnlyList<string> Validate(string root)
    {
        var problems = new List<string>();
        foreach (var json in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            if (json.EndsWith(".meta.json", StringComparison.Ordinal))
            {
                if (!File.Exists(json[..^".meta.json".Length] + ".json")) problems.Add($"orphan meta: {json}");
                continue;
            }
            if (!File.Exists(MetaPath(json))) { problems.Add($"missing meta: {json}"); continue; }
            try
            {
                var meta = JsonSerializer.Deserialize<FixtureMeta>(File.ReadAllText(MetaPath(json)), MetaOptions)!;
                if (string.IsNullOrWhiteSpace(meta.Source) || string.IsNullOrWhiteSpace(meta.SpecSection))
                    problems.Add($"meta needs source and specSection: {json}");
                if (json.Contains($"{Path.DirectorySeparatorChar}errors{Path.DirectorySeparatorChar}") && meta.Binding is null)
                    problems.Add($"error fixture needs binding: {json}");
                using var _ = JsonDocument.Parse(File.ReadAllText(json)); // must-reject files are still valid JSON text
            }
            catch (JsonException e) { problems.Add($"{json}: {e.Message}"); }
        }
        return problems;
    }

    static string MetaPath(string jsonPath) => jsonPath[..^".json".Length] + ".meta.json";
}
