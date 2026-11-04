using System.Reflection;
using System.Xml.Linq;

namespace Ambinder.A2A.PublicApi.Tests;

/// <summary>The shipped assemblies, as a consumer sees them (this test assembly has no InternalsVisibleTo).</summary>
internal static class ShippedAssemblies
{
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>Shipped project names: every src/*/*.csproj that is not IsPackable=false.</summary>
    public static IReadOnlyList<string> ProjectNames { get; } = Directory
        .GetFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
        .Where(IsShipped)
        .Select(Path.GetFileNameWithoutExtension)
        .Select(n => n!)
        .Order(StringComparer.Ordinal)
        .ToArray();

    public static IReadOnlyList<Assembly> All { get; } = ProjectNames
        .Select(n => Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, n + ".dll")))
        .ToArray();

    private static bool IsShipped(string csproj)
    {
        var packable = XDocument.Load(csproj).Descendants("IsPackable").LastOrDefault()?.Value;
        return !string.Equals(packable, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ambinder.A2A.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Ambinder.A2A.slnx not found above the test output folder.");
    }
}
