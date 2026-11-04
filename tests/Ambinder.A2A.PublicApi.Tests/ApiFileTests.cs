namespace Ambinder.A2A.PublicApi.Tests;

/// <summary>Spec 02 section 2: every shipped project tracks its public API in two files next to the csproj.</summary>
public class ApiFileTests
{
    private const string Header = "#nullable enable";

    public static TheoryData<string> Projects()
    {
        var data = new TheoryData<string>();
        foreach (var name in ShippedAssemblies.ProjectNames)
        {
            data.Add(name);
        }

        return data;
    }

    private static string ApiFile(string project, string kind) =>
        Path.Combine(ShippedAssemblies.RepoRoot, "src", project, $"PublicAPI.{kind}.txt");

    [Theory]
    [MemberData(nameof(Projects))]
    public void Every_shipped_project_has_both_api_files(string project)
    {
        foreach (var kind in new[] { "Shipped", "Unshipped" })
        {
            var path = ApiFile(project, kind);
            Assert.True(File.Exists(path), $"{path} is missing. Build the project once; the build creates it.");
            Assert.Equal(Header, File.ReadLines(path).First());
        }
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Shipped_files_are_empty_during_previews(string project)
    {
        var extra = File.ReadLines(ApiFile(project, "Shipped"))
            .Where(l => !string.IsNullOrWhiteSpace(l) && l != Header)
            .ToArray();
        Assert.True(
            extra.Length == 0,
            $"{project}: PublicAPI.Shipped.txt has {extra.Length} line(s) beyond '{Header}'. " +
            "During 1.0.0-preview.N all API lines stay in PublicAPI.Unshipped.txt (spec 02 section 2). " +
            "This test is removed at 1.0.0-rc.1.");
    }
}
