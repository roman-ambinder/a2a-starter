using System.Reflection;
using System.Runtime.CompilerServices;

namespace Ambinder.A2A.PublicApi.Tests;

public sealed class PublicSurfaceTests
{
    /// <summary>
    /// Assembly-name prefixes each package may expose in a public or protected signature, besides the BCL and our own packages.
    /// The rows follow spec 02 section 3 and the package map in spec 01. A new shipped project without a row fails the test.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedForeign = new(StringComparer.Ordinal)
    {
        ["Ambinder.A2A.Protocol"] = [],
        ["Ambinder.A2A.Server"] = ["Microsoft.Extensions."],
        ["Ambinder.A2A.Client"] = ["Microsoft.Extensions."],
        ["Ambinder.A2A.AspNetCore"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore.", "Microsoft.Net.Http.Headers"],
        // gRPC packages: Grpc.AspNetCore / Grpc.Net.Client types only on their own Add.../Map... entry points (checked below), never Google.Protobuf.
        ["Ambinder.A2A.AspNetCore.Grpc"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore.", "Grpc.AspNetCore", "Grpc.Net.Client"],
        ["Ambinder.A2A.Client.Grpc"] = ["Microsoft.Extensions.", "Grpc.Net.Client"],
        ["Ambinder.A2A.Client.Entra"] = ["Microsoft.Extensions.", "Azure.Core"],
        ["Ambinder.A2A.AspNetCore.Entra"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore.", "Azure.Core"],
        ["Ambinder.A2A.Azure.KeyVault"] = ["Microsoft.Extensions.", "Azure.Core"],
        ["Ambinder.A2A.Stores.EntityFrameworkCore"] = ["Microsoft.Extensions.", "Microsoft.EntityFrameworkCore"],
        ["Ambinder.A2A.Stores.EntityFrameworkCore.PostgreSql"] = ["Microsoft.Extensions.", "Microsoft.EntityFrameworkCore", "Npgsql"],
        ["Ambinder.A2A.Stores.EntityFrameworkCore.Sqlite"] = ["Microsoft.Extensions.", "Microsoft.EntityFrameworkCore"],
        ["Ambinder.A2A.Stores.EntityFrameworkCore.SqlServer"] = ["Microsoft.Extensions.", "Microsoft.EntityFrameworkCore"],
        ["Ambinder.A2A.Stores.Cosmos"] = ["Microsoft.Extensions.", "Microsoft.Azure.Cosmos", "Azure.Core"], // Azure.Core: the Cosmos options take a TokenCredential (05 plan, Cosmos options)
        ["Ambinder.A2A.Push.Receiver"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore."],
        ["Ambinder.A2A.Cluster"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore."],
        ["Ambinder.A2A.Cluster.Entra"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore.", "Azure.Core"],
        ["Ambinder.A2A.Extensions.AI"] = ["Microsoft.Extensions."],
        ["Ambinder.A2A.Extensions.AI.AgentFramework"] = ["Microsoft.Extensions.", "Microsoft.Agents.AI"],
        ["Ambinder.A2A.Compat.V0_3"] = ["Microsoft.Extensions."],
        ["Ambinder.A2A.Compat.V0_3.AspNetCore"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore."],
        ["Ambinder.A2A.Testing"] = ["Microsoft.Extensions.", "Microsoft.AspNetCore."],
        ["Ambinder.A2A.Testing.AI"] = ["Microsoft.Extensions."],
        ["Ambinder.A2A.Testing.Conformance"] = ["Microsoft.Extensions.", "xunit."],
    };

    /// <summary>Spec 02 section 3, "internal" rows. A type here that is public fails the test by name.</summary>
    private static readonly string[] MustBeInternal =
    [
        "TaskManager", "TaskEventBroker", "PushNotificationSender", "PushSigningKey", "TracingRequestHandler",
        "PageToken", "TaskStateRules", "Releaser", "RequestValidation", "ServiceParameters", "RestErrorWriter",
        "GrpcErrorMapper", "SseWriter", "AgentCardInterfaces", "WebhookUrlValidator", "A2AGrpcService", "SseReader",
        "RestA2AClient", "GrpcA2AClient", "JsonRpcA2AClient", "TracingA2AClient", "ProtoMapper",
        "UtcTimestampConverter", "A2AJsonContext",
        // ProtoEnumConverter<T> stays public: owner decision 2026-11-04 (Task 2 / Review focus 1). The source generator cannot
        // use an internal converter named in [JsonConverter] on the public enums, so a consumer's own context would not compile.
        // Default implementations (spec 02 section 3, writer's call as a default):
        "InMemoryTaskStore", "InMemoryPushConfigStore", "InMemoryOutboxStore", "InMemoryInstanceStore", "AgentAuthorizer",
        // DefaultOwnerResolver stays public: spec 07 keeps it public so other-scheme resolvers can decorate it.
    ];

    /// <summary>Allowed InternalsVisibleTo targets besides "&lt;assembly&gt;.Tests". Integration.Tests: owner decision 2026-11-03 (Task 3, step 3.1).</summary>
    private static readonly string[] ExtraFriendAssemblies = ["Ambinder.A2A.Benchmarks", "Ambinder.A2A.Integration.Tests"];

    public static IEnumerable<object[]> Assemblies() => ShippedAssemblies.ProjectNames.Select(n => new object[] { n });

    [Fact]
    public void Every_shipped_project_has_an_allow_list_row()
    {
        var missing = ShippedAssemblies.ProjectNames.Where(n => !AllowedForeign.ContainsKey(n)).ToArray();
        Assert.True(missing.Length == 0, $"Add a row to AllowedForeign for: {string.Join(", ", missing)}");
    }

    [Theory(Skip = "Until SDK 02 Task 4 hides the internals"), MemberData(nameof(Assemblies))]
    public void No_forbidden_type_in_a_public_signature(string assemblyName)
    {
        var assembly = ShippedAssemblies.All.Single(a => a.GetName().Name == assemblyName);
        var allowed = AllowedForeign[assemblyName];
        var violations = PublicSignatures.Walk(assembly)
            .Where(use => !IsAllowed(use.Type.Assembly.GetName().Name!, allowed))
            .Select(use => $"{use.Member}: {use.Type.FullName} ({use.Type.Assembly.GetName().Name})")
            .Distinct()
            .ToArray();
        Assert.True(violations.Length == 0, "Forbidden types in public signatures:\n" + string.Join("\n", violations));
    }

    [Theory(Skip = "Until SDK 02 Task 4 hides the internals"), MemberData(nameof(Assemblies))]
    public void Grpc_types_appear_only_on_extension_entry_points_and_options(string assemblyName)
    {
        if (!assemblyName.EndsWith(".Grpc", StringComparison.Ordinal))
        {
            return;
        }

        var assembly = ShippedAssemblies.All.Single(a => a.GetName().Name == assemblyName);
        var types = assembly.GetTypes().Where(PublicSignatures.IsVisible).ToArray();
        var violations = PublicSignatures.Walk(assembly)
            .Where(use => use.Type.Assembly.GetName().Name!.StartsWith("Grpc.", StringComparison.Ordinal))
            .Where(use =>
            {
                var owner = types.Where(t => use.Member == t.FullName || use.Member.StartsWith(t.FullName + ".", StringComparison.Ordinal))
                    .OrderByDescending(t => t.FullName!.Length).FirstOrDefault();
                return owner is null
                    || !((owner.IsAbstract && owner.IsSealed && owner.Name.EndsWith("Extensions", StringComparison.Ordinal))
                         || owner.Name.EndsWith("Options", StringComparison.Ordinal));
            })
            .Select(use => $"{use.Member}: {use.Type.FullName}")
            .Distinct()
            .ToArray();
        Assert.True(violations.Length == 0, "Grpc types outside extension entry points and options:\n" + string.Join("\n", violations));
    }

    [Theory, MemberData(nameof(Assemblies))]
    public void No_public_type_in_an_Internal_namespace(string assemblyName)
    {
        var bad = PublicTypes(assemblyName).Where(t => t.Namespace?.Split('.').Contains("Internal") == true).Select(t => t.FullName);
        Assert.Empty(bad);
    }

    [Theory(Skip = "Until SDK 02 Task 4 hides the internals"), MemberData(nameof(Assemblies))]
    public void Named_plumbing_types_are_not_public(string assemblyName)
    {
        var bad = PublicTypes(assemblyName).Where(t => MustBeInternal.Contains(t.Name)).Select(t => t.FullName);
        Assert.Empty(bad);
    }

    [Theory, MemberData(nameof(Assemblies))]
    public void No_protobuf_type_is_public(string assemblyName)
    {
        var bad = PublicTypes(assemblyName).Where(t => t.Namespace?.StartsWith("Lf.A2a", StringComparison.Ordinal) == true).Select(t => t.FullName);
        Assert.Empty(bad);
    }

    [Theory, MemberData(nameof(Assemblies))]
    public void InternalsVisibleTo_only_to_tests_and_benchmarks(string assemblyName)
    {
        var assembly = ShippedAssemblies.All.Single(a => a.GetName().Name == assemblyName);
        var friends = assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName.Split(',')[0].Trim());
        var bad = friends.Where(f => f != assemblyName + ".Tests" && !ExtraFriendAssemblies.Contains(f)).ToArray();
        Assert.True(bad.Length == 0, $"{assemblyName} has InternalsVisibleTo for: {string.Join(", ", bad)}");
    }

    private static IEnumerable<Type> PublicTypes(string assemblyName)
        => ShippedAssemblies.All.Single(a => a.GetName().Name == assemblyName).GetTypes().Where(PublicSignatures.IsVisible);

    private static bool IsAllowed(string name, string[] allowedForeign)
        => name.StartsWith("Ambinder.A2A.", StringComparison.Ordinal)
           || IsBcl(name)
           || allowedForeign.Any(p => name.StartsWith(p, StringComparison.Ordinal) || (name + ".").StartsWith(p, StringComparison.Ordinal));

    private static bool IsBcl(string name)
        => name is "System.Private.CoreLib" or "netstandard" or "mscorlib"
           || name.StartsWith("System.", StringComparison.Ordinal)
           || name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal);
}
