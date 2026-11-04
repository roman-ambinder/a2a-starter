// Package allow-list and content checks for the shipped NuGet packages.
// Usage: dotnet run eng/check-packages.cs -- --packages artifacts/packages --map eng/package-dependencies.json
// Exit code 0: all good. Exit code 1: one line per problem on stderr. BCL only.
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string? packagesDir = null;
string? mapPath = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--packages" && i + 1 < args.Length) packagesDir = args[++i];
    else if (args[i] == "--map" && i + 1 < args.Length) mapPath = args[++i];
    else { Console.Error.WriteLine($"Unknown argument: {args[i]}"); return 2; }
}
if (packagesDir is null || mapPath is null)
{
    Console.Error.WriteLine("Usage: check-packages.cs --packages <dir> --map <package-dependencies.json>");
    return 2;
}

var problems = new List<string>();
var expected = new Dictionary<string, (HashSet<string> Deps, HashSet<string> Fx)>(StringComparer.Ordinal);
using (var doc = JsonDocument.Parse(File.ReadAllText(mapPath)))
{
    foreach (var p in doc.RootElement.GetProperty("packages").EnumerateObject())
    {
        expected[p.Name] = (ReadSet(p.Value, "dependencies"), ReadSet(p.Value, "frameworkReferences"));
    }
}

string[] forbiddenCore =
[
    @"^Grpc\.", @"^Google\.", @"^Azure\.", @"^Microsoft\.AspNetCore\.", @"^Microsoft\.EntityFrameworkCore",
    @"^Npgsql", @"^Microsoft\.Data\.", @"^Microsoft\.Azure\.",
];
string[] corePackages = ["Ambinder.A2A.Protocol", "Ambinder.A2A.Server", "Ambinder.A2A.Client"];

var rows = new List<(string Id, string Version, double SizeKb, int DepCount)>();
var found = new Dictionary<string, string>(StringComparer.Ordinal); // id -> version

foreach (var path in Directory.GetFiles(packagesDir, "*.nupkg").OrderBy(p => p, StringComparer.Ordinal))
{
    var file = Path.GetFileName(path);
    using var zip = ZipFile.OpenRead(path);
    var nuspecEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal) && !e.FullName.Contains('/'));
    if (nuspecEntry is null) { problems.Add($"{file}: no .nuspec in the package"); continue; }

    XDocument nuspec;
    using (var s = nuspecEntry.Open()) nuspec = XDocument.Load(s);
    var meta = nuspec.Root!.Elements().First(e => e.Name.LocalName == "metadata");
    string? Meta(string name) => meta.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    var id = Meta("id") ?? "";
    var version = Meta("version") ?? "";
    found[id] = version;

    // Snupkg next to it.
    if (!File.Exists(Path.Combine(packagesDir, $"{id}.{version}.snupkg")))
        problems.Add($"{id}: missing symbol package {id}.{version}.snupkg");

    // Dependencies from all groups.
    var deps = meta.Descendants().Where(e => e.Name.LocalName == "dependency")
        .Select(e => e.Attribute("id")!.Value).ToHashSet(StringComparer.Ordinal);
    var fx = meta.Descendants().Where(e => e.Name.LocalName == "frameworkReference")
        .Select(e => e.Attribute("name")!.Value).ToHashSet(StringComparer.Ordinal);
    rows.Add((id, version, new FileInfo(path).Length / 1024.0, deps.Count));

    if (expected.TryGetValue(id, out var exp))
    {
        foreach (var d in deps.Except(exp.Deps).OrderBy(x => x, StringComparer.Ordinal))
            problems.Add($"{id}: dependency {d} is in the package but not in {Path.GetFileName(mapPath)}");
        foreach (var d in exp.Deps.Except(deps).OrderBy(x => x, StringComparer.Ordinal))
            problems.Add($"{id}: dependency {d} is in {Path.GetFileName(mapPath)} but not in the package");
        foreach (var f in fx.Except(exp.Fx).OrderBy(x => x, StringComparer.Ordinal))
            problems.Add($"{id}: frameworkReference {f} is in the package but not in {Path.GetFileName(mapPath)}");
        foreach (var f in exp.Fx.Except(fx).OrderBy(x => x, StringComparer.Ordinal))
            problems.Add($"{id}: frameworkReference {f} is in {Path.GetFileName(mapPath)} but not in the package");
    }

    if (corePackages.Contains(id))
    {
        foreach (var d in deps.OrderBy(x => x, StringComparer.Ordinal))
            if (forbiddenCore.Any(pat => Regex.IsMatch(d, pat)))
                problems.Add($"{id}: core package must not depend on {d}");
        foreach (var f in fx)
            problems.Add($"{id}: core package must not have a frameworkReference ({f})");
    }

    // Content.
    var entries = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
    foreach (var required in new[] { "LICENSE", "NOTICE", "lib/net10.0/" + id + ".dll", "lib/net10.0/" + id + ".xml" })
        if (!entries.Contains(required)) problems.Add($"{id}: missing {required}");

    var readme = Meta("readme");
    if (string.IsNullOrEmpty(readme)) problems.Add($"{id}: nuspec has no <readme>");
    else if (!entries.Contains(readme.Replace('\\', '/'))) problems.Add($"{id}: readme file {readme} is not in the package");
    var icon = Meta("icon");
    if (string.IsNullOrEmpty(icon)) problems.Add($"{id}: nuspec has no <icon>");
    else if (!entries.Contains(icon.Replace('\\', '/'))) problems.Add($"{id}: icon file {icon} is not in the package");

    var license = meta.Elements().FirstOrDefault(e => e.Name.LocalName == "license");
    if (license is null || license.Attribute("type")?.Value != "expression" || license.Value != "Apache-2.0")
        problems.Add($"{id}: license expression must be Apache-2.0 (found '{license?.Value ?? "none"}')");

    var repo = meta.Elements().FirstOrDefault(e => e.Name.LocalName == "repository");
    if (string.IsNullOrEmpty(repo?.Attribute("commit")?.Value))
        problems.Add($"{id}: nuspec repository has no commit");
}

// Allow-list vs. packed set.
foreach (var id in found.Keys.Except(expected.Keys).OrderBy(x => x, StringComparer.Ordinal))
    problems.Add($"{id}: package is not listed in {Path.GetFileName(mapPath)}");
foreach (var id in expected.Keys.Except(found.Keys).OrderBy(x => x, StringComparer.Ordinal))
    problems.Add($"{id}: listed in {Path.GetFileName(mapPath)} but no .nupkg was packed");

// Summary.
var sb = new StringBuilder();
sb.AppendLine("## Package check");
sb.AppendLine();
sb.AppendLine("| ID | Version | nupkg size (KB) | Dependencies |");
sb.AppendLine("|---|---|---:|---:|");
foreach (var r in rows.OrderBy(r => r.Id, StringComparer.Ordinal))
    sb.AppendLine($"| {r.Id} | {r.Version} | {r.SizeKb:F1} | {r.DepCount} |");
sb.AppendLine();
sb.AppendLine(problems.Count == 0 ? "Result: OK" : $"Result: {problems.Count} problem(s)");
var summaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
if (!string.IsNullOrEmpty(summaryFile)) File.AppendAllText(summaryFile, sb.ToString());
else Console.Out.Write(sb.ToString());

foreach (var p in problems) Console.Error.WriteLine(p);
return problems.Count == 0 ? 0 : 1;

static HashSet<string> ReadSet(JsonElement e, string name) =>
    e.TryGetProperty(name, out var arr)
        ? arr.EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal)
        : new HashSet<string>(StringComparer.Ordinal);
