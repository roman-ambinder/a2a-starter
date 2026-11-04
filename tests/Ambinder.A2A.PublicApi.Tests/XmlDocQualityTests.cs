using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Ambinder.A2A.PublicApi.Tests;

/// <summary>
/// Lints the XML documentation of the public and protected API (spec 02 section 6). The compiler already fails the build for a
/// missing doc comment (CS1591) and for malformed XML. These tests check what the compiler does not: empty or name-only
/// summaries, missing param, returns and typeparam tags, a misplaced inheritdoc, unresolved crefs and missing examples.
/// </summary>
public partial class XmlDocQualityTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Entry points that need an <c>&lt;example&gt;</c> (Task 10). A name that does not exist yet is listed in the test output.</summary>
    private static readonly string[] ExamplePrefixes =
    [
        "M:Microsoft.Extensions.DependencyInjection.A2AServerServiceCollectionExtensions.AddA2AServer(",
        "M:Microsoft.Extensions.DependencyInjection.A2AServerCoreServiceCollectionExtensions.AddA2AServerCore(",
        "M:Ambinder.A2A.Server.Hosting.A2AServerBuilder.AddAgent``1(",
        "M:Microsoft.AspNetCore.Builder.A2AEndpointRouteBuilderExtensions.MapA2A(",
        "T:Ambinder.A2A.Server.IAgentHandler",
        "M:Ambinder.A2A.Client.IA2AClient.SendMessageAsync(",
        "M:Ambinder.A2A.Client.IA2AClient.SendStreamingMessageAsync(",
    ];

    /// <summary>Names that do not exist yet (they arrive with a later package). Skipped, and listed in the test output.</summary>
    private static readonly string[] PendingExampleNames = ["AddA2AClient"];

    public static TheoryData<string> Projects()
    {
        var data = new TheoryData<string>();
        foreach (var name in ShippedAssemblies.ProjectNames)
        {
            data.Add(name);
        }

        return data;
    }

    private static Dictionary<string, XElement> LoadDocs(string project)
    {
        var path = Path.Combine(AppContext.BaseDirectory, project + ".xml");
        Assert.True(File.Exists(path), $"{path} is missing. The project must set GenerateDocumentationFile and the test project must reference it.");
        return XDocument.Load(path).Descendants("member").ToDictionary(m => (string)m.Attribute("name")!, m => m, StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Public_api_docs_are_complete_and_useful(string project)
    {
        var docs = LoadDocs(project);
        var assembly = ShippedAssemblies.All.Single(a => a.GetName().Name == project);
        var problems = new List<string>();

        foreach (var type in assembly.GetTypes().Where(PublicSignatures.IsVisible).Where(t => !IsCompilerGenerated(t)))
        {
            Check(type, docs, problems);
            var isRecord = type.GetMethod("<Clone>$", Declared) is not null;
            foreach (var member in type.GetMembers(Declared))
            {
                if (member is Type || !IsVisibleMember(member) || IsCompilerGenerated(member) || IsAccessor(member)) continue;
                if (isRecord && IsRecordSynthesized(member)) continue;
                Check(member, docs, problems);
            }
        }

        Assert.True(problems.Count == 0, $"{project}: {problems.Count} documentation problem(s):{Environment.NewLine}{string.Join(Environment.NewLine, problems.Order(StringComparer.Ordinal))}");
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Docs_have_no_unresolved_crefs(string project)
    {
        var bad = LoadDocs(project).Values
            .SelectMany(m => m.Descendants().Where(e => ((string?)e.Attribute("cref"))?.StartsWith("!:", StringComparison.Ordinal) == true)
                .Select(e => $"{(string)m.Attribute("name")!}: cref=\"{(string)e.Attribute("cref")!}\" does not resolve"))
            .ToArray();
        Assert.True(bad.Length == 0, string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void Entry_points_have_examples()
    {
        var all = ShippedAssemblies.ProjectNames.SelectMany(p => LoadDocs(p)).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.First().Value);
        var missing = new List<string>();
        foreach (var prefix in ExamplePrefixes)
        {
            var hits = all.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            if (hits.Length == 0) missing.Add($"{prefix}: no documented member found. Fix the entry point list in this test.");
            missing.AddRange(hits.Where(h => h.Value.Element("example") is not { } ex || string.IsNullOrWhiteSpace(ex.Value)).Select(h => $"{h.Key}: no <example>"));
        }

        var skipped = PendingExampleNames.Where(n => !all.Keys.Any(k => k.Contains("." + n + "(", StringComparison.Ordinal))).ToArray();
        foreach (var name in skipped)
        {
            TestContext.Current.SendDiagnosticMessage($"Entry point '{name}' does not exist yet; its example check is skipped.");
        }

        var existingPending = PendingExampleNames.Except(skipped).SelectMany(n => all.Where(kv => kv.Key.Contains("." + n + "(", StringComparison.Ordinal)))
            .Where(kv => kv.Value.Element("example") is null).Select(kv => $"{kv.Key}: no <example>");
        missing.AddRange(existingPending);
        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    private static void Check(MemberInfo member, Dictionary<string, XElement> docs, List<string> problems)
    {
        var id = DocIds.For(member);
        if (!docs.TryGetValue(id, out var doc))
        {
            // The compiler writes no entry for an implicit parameterless constructor.
            if (member is ConstructorInfo { IsStatic: false } c && c.GetParameters().Length == 0) return;
            problems.Add($"{id}: no documentation entry");
            return;
        }

        var inherits = doc.Element("inheritdoc");
        if (inherits is not null)
        {
            if (inherits.Attribute("cref") is null && !InheritsFromSomething(member))
                problems.Add($"{id}: <inheritdoc/> without cref, but the member neither overrides nor implements anything");
            return;
        }

        var summary = Text(doc.Element("summary"));
        if (summary.Length == 0)
        {
            problems.Add($"{id}: <summary> is missing or empty");
        }
        else if (RestatesName(member, summary))
        {
            problems.Add($"{id}: <summary> only repeats the member name ('{summary}')");
        }

        if (member is MethodBase method)
        {
            var declared = method.GetParameters().Select(p => p.Name!).ToHashSet(StringComparer.Ordinal);
            var documented = doc.Elements("param").Select(p => (string?)p.Attribute("name") ?? "").ToArray();
            problems.AddRange(declared.Except(documented).Select(n => $"{id}: no <param> for '{n}'"));
            problems.AddRange(documented.Except(declared).Select(n => $"{id}: <param name=\"{n}\"> matches no parameter"));

            if (method is MethodInfo mi && mi.ReturnType != typeof(void) && doc.Element("returns") is null)
                problems.Add($"{id}: no <returns>");

            if (method.IsGenericMethodDefinition)
                CheckTypeParams(id, method.GetGenericArguments(), doc, problems);
        }
        else if (member is Type { IsGenericTypeDefinition: true } type)
        {
            CheckTypeParams(id, type.GetGenericArguments(), doc, problems);
        }
    }

    private static void CheckTypeParams(string id, Type[] parameters, XElement doc, List<string> problems)
    {
        var documented = doc.Elements("typeparam").Select(p => (string?)p.Attribute("name") ?? "").ToHashSet(StringComparer.Ordinal);
        problems.AddRange(parameters.Select(p => p.Name).Where(n => !documented.Contains(n)).Select(n => $"{id}: no <typeparam> for '{n}'"));
    }

    private static string Text(XElement? element)
        => element is null ? "" : Whitespace().Replace(element.Value, " ").Trim();

    /// <summary>True when the summary is the member name in words, optionally after "Gets or sets the" and so on.</summary>
    private static bool RestatesName(MemberInfo member, string summary)
    {
        var name = member is ConstructorInfo ? member.DeclaringType!.Name : member.Name;
        var words = Normalize(Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " "));
        var text = Normalize(Regex.Replace(summary, "^(gets or sets|gets|sets)( the)?\\s+", "", RegexOptions.IgnoreCase));
        return text == words || text == "the " + words;
    }

    private static string Normalize(string s) => Whitespace().Replace(s.TrimEnd('.', ' ').ToLowerInvariant(), " ").Trim();

    private static bool InheritsFromSomething(MemberInfo member)
    {
        var type = member.DeclaringType!;
        switch (member)
        {
            case MethodInfo m:
                if (m.GetBaseDefinition().DeclaringType != type) return true;
                return type.GetInterfaces().Any(i => type.GetInterfaceMap(i).TargetMethods.Contains(m));
            case PropertyInfo p:
                var get = p.GetMethod ?? p.SetMethod;
                return get is not null && InheritsFromSomething(get);
            case Type t:
                return (t.BaseType is not null && t.BaseType != typeof(object) && t.BaseType != typeof(ValueType) && t.BaseType != typeof(Enum)) || t.GetInterfaces().Length > 0;
            default:
                return false;
        }
    }

    private static bool IsVisibleMember(MemberInfo m) => m switch
    {
        MethodBase mb => mb.IsPublic || mb.IsFamily || mb.IsFamilyOrAssembly,
        FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
        PropertyInfo p => (p.GetMethod ?? p.SetMethod) is { } a && (a.IsPublic || a.IsFamily || a.IsFamilyOrAssembly),
        EventInfo e => e.AddMethod is { } a && (a.IsPublic || a.IsFamily || a.IsFamilyOrAssembly),
        _ => false,
    };

    private static bool IsAccessor(MemberInfo m)
        => m is MethodInfo { IsSpecialName: true } mi && (mi.Name.StartsWith("get_", StringComparison.Ordinal) || mi.Name.StartsWith("set_", StringComparison.Ordinal)
            || mi.Name.StartsWith("add_", StringComparison.Ordinal) || mi.Name.StartsWith("remove_", StringComparison.Ordinal))
           || (m is FieldInfo { IsSpecialName: true });

    private static bool IsCompilerGenerated(MemberInfo m) => m.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null;

    /// <summary>Members the compiler writes for a record. They carry no doc entry and need none.</summary>
    private static bool IsRecordSynthesized(MemberInfo m)
        => m.Name is "Equals" or "GetHashCode" or "ToString" or "PrintMembers" or "Deconstruct" or "<Clone>$" or "EqualityContract" or "op_Equality" or "op_Inequality"
           || (m is ConstructorInfo c && c.GetParameters() is [{ } only] && only.ParameterType == c.DeclaringType);

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();
}
