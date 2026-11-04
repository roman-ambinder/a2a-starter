namespace Ambinder.A2A.PublicApi.Tests;

public sealed class ShippedAssembliesTests
{
    [Fact]
    public void All_shipped_projects_are_referenced()
    {
        var missing = ShippedAssemblies.ProjectNames
            .Where(name => !File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".dll")))
            .Select(name => $"Add a ProjectReference to tests/Ambinder.A2A.PublicApi.Tests for {name}.")
            .ToArray();
        Assert.True(missing.Length == 0, string.Join("\n", missing));
    }
}
