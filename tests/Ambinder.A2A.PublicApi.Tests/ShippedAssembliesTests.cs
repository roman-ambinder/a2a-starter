namespace Ambinder.A2A.PublicApi.Tests;

public sealed class ShippedAssembliesTests
{
    [Fact]
    public void All_shipped_projects_are_referenced()
    {
        var missing = ShippedAssemblies.ProjectNames
            .Where(n => !File.Exists(Path.Combine(AppContext.BaseDirectory, n + ".dll")))
            .Select(n => $"Add a ProjectReference to tests/Ambinder.A2A.PublicApi.Tests for {n}.")
            .ToArray();
        Assert.True(missing.Length == 0, string.Join("\n", missing));
    }

    [Fact]
    public void Shared_source_folders_are_not_shipped_projects()
    {
        Assert.DoesNotContain("Shared", ShippedAssemblies.ProjectNames);
        Assert.DoesNotContain("Grpc.Shared", ShippedAssemblies.ProjectNames);
        Assert.NotEmpty(ShippedAssemblies.ProjectNames);
    }
}
