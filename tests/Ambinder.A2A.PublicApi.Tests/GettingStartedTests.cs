using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.PublicApi.Tests;

/// <summary>Goal 9: a developer's first program needs few <c>using</c> lines (the samples compile as part of the solution build).</summary>
public sealed class GettingStartedTests
{
    [Theory]
    [InlineData("samples/GettingStarted.Agent")]
    [InlineData("samples/GettingStarted.Client")]
    public void Sample_uses_at_most_two_Ambinder_namespaces(string sampleFolder)
    {
        var root = Path.Combine(ShippedAssemblies.RepoRoot, sampleFolder);
        var count = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(File.ReadAllLines)
            .Count(line => line.StartsWith("using Ambinder.A2A", StringComparison.Ordinal) || line.StartsWith("global using Ambinder.A2A", StringComparison.Ordinal));
        Assert.True(count <= 2, $"{sampleFolder} has {count} 'using Ambinder.A2A' lines; the limit is 2.");
    }

    [Fact]
    public void AddA2AServer_registers_every_service_AddA2AServerCore_registers()
    {
        var core = new ServiceCollection();
        core.AddA2AServerCore(o => o.PublicBaseUrl = "http://localhost:5100");
        var web = new ServiceCollection();
        web.AddA2AServer(o => o.PublicBaseUrl = "http://localhost:5100");

        var webTypes = web.Select(d => d.ServiceType).ToHashSet();
        var missing = core.Select(d => d.ServiceType).Where(t => !webTypes.Contains(t)).ToArray();
        Assert.Empty(missing);
    }
}
