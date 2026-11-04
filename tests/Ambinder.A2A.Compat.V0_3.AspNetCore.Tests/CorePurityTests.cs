using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ambinder.A2A.Compat.V0_3.AspNetCore.Tests;

/// <summary>
/// Core purity (plan Task 3, spec "Core purity"): the six core assemblies and their sources must contain no A2A 0.3 name.
/// The forbidden list is derived from <c>tests/vectors/v0_3/inventory.json</c> at test time.
/// </summary>
public sealed class CorePurityTests
{
    private static readonly string[] CoreProjects =
    [
        "Ambinder.A2A.Protocol", "Ambinder.A2A.Server", "Ambinder.A2A.AspNetCore",
        "Ambinder.A2A.AspNetCore.Grpc", "Ambinder.A2A.Client", "Ambinder.A2A.Client.Grpc",
    ];

    /// <summary>Names the plan lists explicitly; the derived list must contain every one of them.</summary>
    private static readonly string[] PlanForbidden =
    [
        "message/send", "message/stream", "tasks/resubscribe", "tasks/pushNotificationConfig/",
        "agent/getAuthenticatedExtendedCard", "status-update", "artifact-update", "additionalInterfaces",
        "preferredTransport", "input-required", "auth-required",
    ];

    /// <summary>
    /// Allow-list for assembly hits: (assembly file, forbidden name) to reason. A hit that comes from the
    /// embedded 1.0 proto descriptor is added here only with the proto/a2a.proto line that proves it is a genuine 1.0 field.
    /// </summary>
    private static readonly Dictionary<(string Assembly, string Name), string> AssemblyAllowList = new()
    {
        // Owner decision 2026-11-04: these are the a2a.task.state telemetry attribute values (A2ATelemetryNames.StateValue),
        // not wire values. docs/superpowers/specs/2026-11-03-observability-design.md lines 77 and 101 name them as span-event states.
        [("Ambinder.A2A.Protocol", "input-required")] = "Telemetry attribute value, observability design lines 77 and 101; owner-approved 2026-11-04.",
        [("Ambinder.A2A.Protocol", "auth-required")] = "Telemetry attribute value, observability design lines 77 and 101; owner-approved 2026-11-04.",
    };

    /// <summary>
    /// Allow-list for the version-branch grep: a source file name and the trimmed text of the allowed line, with the reason.
    /// A line is never allowed without a reason.
    /// </summary>
    private static readonly (string File, string Line, string Reason)[] VersionLiteralAllowList =
    [
        ("A2AClientFactory.cs",
         "if (interfaces.Any(i => IsMinor(i.ProtocolVersion, \"0.3\")))",
         "Selects the documented missing-package hint (\"add the Ambinder.A2A.Compat.V0_3 package\") appended to the NotSupportedException message on the next line; it changes message text only, never behavior."),
        ("ServiceParameters.cs",
         "if (string.IsNullOrWhiteSpace(raw)) return \"0.3\";",
         "Owner-approved 2026-11-04. Spec 3.6.2: a request without the version header is treated as 0.3. NormalizeVersion only names the version handed to IA2AVersionDispatcher, which compat implements; the core serves no 0.3 behavior here."),
    ];

    [Fact]
    public void Forbidden_list_covers_the_plan_list_and_leaves_out_1_0_names()
    {
        var forbidden = Forbidden.Value.Substrings;
        foreach (var name in PlanForbidden)
            Assert.Contains(name, forbidden);
        Assert.Contains("TaskNotFoundError", Forbidden.Value.Whole);

        // Names that exist in 1.0 (proto/a2a.proto) must never be forbidden.
        Assert.DoesNotContain("taskId", forbidden);
        Assert.DoesNotContain("SendMessageRequest", forbidden);
        Assert.DoesNotContain("pushNotificationConfig", forbidden);
    }

    [Theory]
    [MemberData(nameof(CoreAssemblyNames))]
    public void Core_assembly_contains_no_0_3_name(string project)
    {
        var path = Path.Combine(AppContext.BaseDirectory, project + ".dll");
        Assert.True(File.Exists(path), $"{path} was not copied to the test output; add a project reference.");

        using var file = File.OpenRead(path);
        var hits = PurityScanner.ScanAssembly(file, Forbidden.Value.Substrings, Forbidden.Value.Whole)
            .Where(h => !AssemblyAllowList.ContainsKey((project, h.Name)))
            .ToList();

        Assert.True(hits.Count == 0, $"{project}.dll contains 0.3 names:{Environment.NewLine}{string.Join(Environment.NewLine, hits)}");
    }

    [Fact]
    public void No_core_project_references_a_compat_project()
    {
        var root = RepoRoot.Value;
        var offenders = new List<string>();
        foreach (var project in CoreProjects)
        {
            var dir = Path.Combine(root, "src", project);
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                         .Where(f => !IsBuildOutput(f) && (f.EndsWith(".csproj", StringComparison.Ordinal) || f.EndsWith(".props", StringComparison.Ordinal) || f.EndsWith(".targets", StringComparison.Ordinal))))
            {
                var text = File.ReadAllText(file);
                if (text.Contains("Compat.V0_3", StringComparison.OrdinalIgnoreCase)) // project reference, linked Compat.V0_3.Shared, or package reference
                    offenders.Add(Path.GetRelativePath(root, file));
            }
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "Shared"), "*.cs", SearchOption.AllDirectories).Where(f => !IsBuildOutput(f)))
        {
            if (File.ReadAllText(file).Contains("Compat.V0_3", StringComparison.Ordinal))
                offenders.Add(Path.GetRelativePath(root, file));
        }

        Assert.True(offenders.Count == 0, "Core must not reference or link compat code:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void No_core_source_branches_on_the_0_3_version()
    {
        var root = RepoRoot.Value;
        var offenders = new List<string>();
        var folders = CoreProjects.Select(p => Path.Combine(root, "src", p)).Append(Path.Combine(root, "src", "Shared"));
        foreach (var folder in folders)
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).Where(f => !IsBuildOutput(f)))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var trimmed = lines[i].Trim();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*') || trimmed.StartsWith("/*", StringComparison.Ordinal))
                        continue;
                    if (!PurityScanner.QuotedVersion.IsMatch(trimmed))
                        continue;
                    var name = Path.GetFileName(file);
                    if (VersionLiteralAllowList.Any(a => a.File == name && a.Line == trimmed && a.Reason.Length > 0))
                        continue;
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {trimmed}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Core source holds a \"0.3\" version literal:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // ---- The scanner itself: prove it can fail. ----

    [Fact]
    public void Scanner_finds_a_forbidden_user_string_type_name_and_member_name()
    {
        var forbidden = new[] { "message/send", "preferredTransport", "additionalInterfaces" };
        using var stream = BuildSampleAssembly();

        var hits = PurityScanner.ScanAssembly(stream, forbidden);

        Assert.Contains(hits, h => h.Name == "message/send" && h.Where.StartsWith("user string", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Name == "preferredTransport" && h.Where.StartsWith("name", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Name == "additionalInterfaces" && h.Where.StartsWith("descriptor", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_matches_whole_names_exactly()
    {
        using var stream = BuildSampleAssembly();
        Assert.Contains(PurityScanner.ScanAssembly(stream, [], ["Holder"]), h => h.Name == "Holder");
        stream.Position = 0;
        Assert.Empty(PurityScanner.ScanAssembly(stream, [], ["Hold"]));
    }

    [Fact]
    public void Scanner_passes_a_clean_assembly()
    {
        using var stream = BuildSampleAssembly();
        Assert.Empty(PurityScanner.ScanAssembly(stream, ["tasks/resubscribe", "input-required"]));
    }

    [Fact]
    public void Version_literal_pattern_matches_only_quoted_0_3()
    {
        Assert.Matches(PurityScanner.QuotedVersion, "if (v == \"0.3\") {}");
        Assert.Matches(PurityScanner.QuotedVersion, "var v = \"0.3.0\";");
        Assert.DoesNotMatch(PurityScanner.QuotedVersion, "var m = \"treated as A2A 0.3\";");
        Assert.DoesNotMatch(PurityScanner.QuotedVersion, "var v = \"1.0\";");
    }

    public static TheoryData<string> CoreAssemblyNames() => [.. CoreProjects];

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>
    /// Builds a tiny assembly with (a) the user string "message/send", (b) a field named "preferredTransport", and
    /// (c) a base64 chunk that decodes to text holding "additionalInterfaces", like a protobuf file descriptor.
    /// </summary>
    private static MemoryStream BuildSampleAssembly()
    {
        var builder = new PersistedAssemblyBuilder(new System.Reflection.AssemblyName("PuritySample"), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("PuritySample");
        var type = module.DefineType("Sample.Holder", System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);
        type.DefineField("preferredTransport", typeof(int), System.Reflection.FieldAttributes.Public);
        var method = type.DefineMethod("Names", System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static, typeof(string), Type.EmptyTypes);
        var il = method.GetILGenerator();
        var descriptor = Convert.ToBase64String(Encoding.UTF8.GetBytes("\n\u0014xx additionalInterfaces xx field list padding"));
        il.Emit(OpCodes.Ldstr, "message/send");
        il.Emit(OpCodes.Ldstr, descriptor);
        il.Emit(OpCodes.Call, typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Ret);
        type.CreateType();

        var metadata = builder.GenerateMetadata(out var ilStream, out var fieldData);
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: System.Reflection.PortableExecutable.Characteristics.Dll),
            new MetadataRootBuilder(metadata), ilStream, fieldData);
        var blob = new System.Reflection.Metadata.BlobBuilder();
        peBuilder.Serialize(blob);
        var stream = new MemoryStream();
        blob.WriteContentTo(stream);
        stream.Position = 0;
        return stream;
    }

    private static readonly Lazy<string> RepoRoot = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("*.slnx").Length == 0)
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No .slnx found above " + AppContext.BaseDirectory);
    });

    /// <summary>
    /// The 0.3-only names. Provenance: <c>tests/vectors/v0_3/inventory.json</c> (extracted from the vendored v0.3.0 spec files):
    /// JSON-RPC methods, REST paths, task state values, kind values, AgentCard/message/event field names, error type names and gRPC
    /// RPC names. Dropped: any name that is also a 1.0 identifier in <c>proto/a2a.proto</c> (exact, or as snake_case turned to
    /// camelCase) or that appears literally in that file. Also dropped: plain one-word names ("working", "kind", "url"), which are
    /// everyday words and cannot be searched as substrings; only names with a separator (<c>/ - : { }</c>) or an inner capital stay.
    /// </summary>
    private static readonly Lazy<ForbiddenNames> Forbidden = new(() =>
    {
        var root = RepoRoot.Value;
        var proto = File.ReadAllText(Path.Combine(root, "proto", "a2a.proto"));
        var protoNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(proto, "[A-Za-z_][A-Za-z0-9_]*"))
        {
            protoNames.Add(m.Value);
            if (m.Value.Contains('_', StringComparison.Ordinal) && m.Value.ToUpperInvariant() != m.Value)
                protoNames.Add(SnakeToCamel(m.Value));
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "vectors", "v0_3", "inventory.json")));
        var inv = doc.RootElement;
        var candidates = new List<string>();
        Collect(inv.GetProperty("jsonRpcMethods").GetProperty("methods"), "method", candidates);
        Collect(inv.GetProperty("restEndpoints").GetProperty("endpoints"), "path", candidates);
        Collect(inv.GetProperty("taskStates").GetProperty("values"), "json", candidates);
        Collect(inv.GetProperty("kinds").GetProperty("values"), "kind", candidates);
        // Type-like names (A2A error types, gRPC RPC names) are matched whole, never as substrings: "GetAgentCard" is
        // a prefix of the 1.0 client's GetAgentCardAsync. Standard JSON-RPC errors (InternalError, ...) are shared with 1.0.
        var exact = new List<string>();
        foreach (var e in inv.GetProperty("errorCodes").GetProperty("errors").EnumerateArray())
        {
            if (e.GetProperty("category").GetString() != "standard JSON-RPC")
                exact.Add(e.GetProperty("name").GetString()!);
        }

        Collect(inv.GetProperty("grpcRpcs").GetProperty("rpcs"), "rpc", exact);
        Collect(inv.GetProperty("agentCard").GetProperty("fields"), "field", candidates);
        foreach (var sub in inv.GetProperty("agentCard").GetProperty("subObjects").EnumerateObject())
            CollectFrom(sub.Value, "fields", "field", candidates);
        foreach (var type in inv.GetProperty("messageAndTaskFields").EnumerateObject())
            CollectFrom(type.Value, "fields", "field", candidates);
        foreach (var cfg in inv.GetProperty("pushNotifications").GetProperty("config").EnumerateObject())
        {
            if (cfg.Value.ValueKind == JsonValueKind.Object && cfg.Value.TryGetProperty("json", out var json))
                Collect(json, "field", candidates);
        }

        // The prefix-style names the plan lists; the inventory holds the full method names.
        candidates.AddRange(PlanForbidden);

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in candidates.Distinct(StringComparer.Ordinal))
        {
            var distinctive = name.IndexOfAny(['/', '-', ':', '{', '}']) >= 0 || name.Skip(1).Any(char.IsUpper);
            if (!distinctive || protoNames.Contains(name) || proto.Contains(name, StringComparison.Ordinal))
                continue;
            result.Add(name);
        }

        return new ForbiddenNames(result, exact.Distinct(StringComparer.Ordinal).Where(n => !protoNames.Contains(n)).ToHashSet(StringComparer.Ordinal));
    });

    /// <summary>The derived 0.3-only names: <see cref="Substrings"/> match anywhere, <see cref="Whole"/> only as a complete name.</summary>
    private sealed record ForbiddenNames(IReadOnlySet<string> Substrings, IReadOnlySet<string> Whole);

    private static void CollectFrom(JsonElement obj, string arrayProperty, string property, List<string> into)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(arrayProperty, out var array))
            Collect(array, property, into);
    }

    private static void Collect(JsonElement array, string property, List<string> into)
    {
        if (array.ValueKind != JsonValueKind.Array) return;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String)
                into.Add(v.GetString()!);
        }
    }

    private static string SnakeToCamel(string snake)
    {
        var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}

/// <summary>Scans a .NET assembly's metadata for forbidden names.</summary>
internal static class PurityScanner
{
    /// <summary>A quoted <c>"0.3"</c> or <c>"0.3.x"</c> version literal.</summary>
    internal static readonly Regex QuotedVersion = new("\"0\\.3[.\"]", RegexOptions.CultureInvariant);

    /// <summary>One forbidden name found in an assembly.</summary>
    internal sealed record Hit(string Name, string Where)
    {
        public override string ToString() => $"'{Name}' in {Where}";
    }

    /// <summary>
    /// Scans the user-string heap (<c>#US</c>), every name in the <c>#Strings</c> heap (types, namespaces, members, parameters,
    /// resources; <paramref name="wholeNames"/> only match a complete name) and the decoded base64 text of string literals (protobuf file descriptors are stored that way).
    /// </summary>
    internal static List<Hit> ScanAssembly(Stream assembly, IEnumerable<string> forbidden, IEnumerable<string>? wholeNames = null)
    {
        var names = forbidden.ToArray();
        var whole = (wholeNames ?? []).ToArray();
        var hits = new List<Hit>();
        using var pe = new PEReader(assembly, PEStreamOptions.LeaveOpen);
        var md = pe.GetMetadataReader();

        for (var h = md.GetNextHandle(MetadataTokens.UserStringHandle(0)); !h.IsNil; h = md.GetNextHandle(h))
        {
            var s = md.GetUserString(h);
            foreach (var n in names)
            {
                if (s.Contains(n, StringComparison.Ordinal))
                    hits.Add(new Hit(n, $"user string \"{Trim(s)}\""));
            }

            var decoded = TryDecodeBase64(s);
            if (decoded is null) continue;
            foreach (var n in names)
            {
                if (decoded.Contains(n, StringComparison.Ordinal))
                    hits.Add(new Hit(n, $"descriptor (base64 literal) \"{Trim(s)}\""));
            }
        }

        for (var h = md.GetNextHandle(MetadataTokens.StringHandle(0)); !h.IsNil; h = md.GetNextHandle(h))
        {
            var s = md.GetString(h);
            foreach (var n in names)
            {
                if (s.Contains(n, StringComparison.Ordinal))
                    hits.Add(new Hit(n, $"name \"{s}\""));
            }

            foreach (var n in whole)
            {
                if (string.Equals(s, n, StringComparison.Ordinal))
                    hits.Add(new Hit(n, $"name \"{s}\""));
            }
        }

        return hits;
    }

    private static string Trim(string s) => s.Length <= 120 ? s : s[..120] + "...";

    /// <summary>Decodes a base64-looking literal to Latin-1 text, tolerating a chunk that is cut off; null when it is not base64.</summary>
    private static string? TryDecodeBase64(string s)
    {
        if (s.Length < 16) return null;
        foreach (var c in s)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '='))
                return null;
        }

        var usable = s.TrimEnd('=');
        usable = usable[..(usable.Length / 4 * 4)];
        try
        {
            return Encoding.Latin1.GetString(Convert.FromBase64String(usable));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
