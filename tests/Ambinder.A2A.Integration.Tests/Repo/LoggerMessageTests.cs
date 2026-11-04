using System.Text.RegularExpressions;

namespace Ambinder.A2A.Integration.Tests.Repo;

/// <summary>
/// Each shipped assembly compiles at most one source file with <c>[LoggerMessage]</c> methods. The logging source generator
/// writes separate declarations in an order that changes from build to build, which breaks the <c>reproducibility</c> CI job.
/// </summary>
public sealed class LoggerMessageTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ambinder.A2A.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Ambinder.A2A.slnx not found above " + AppContext.BaseDirectory);
    }

    public static TheoryData<string> ShippedProjects()
    {
        var data = new TheoryData<string>();
        foreach (var csproj in Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(RepoRoot(), csproj).Replace('\\', '/'));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ShippedProjects))]
    public void At_most_one_LoggerMessage_file_per_assembly(string project)
    {
        var csproj = Path.Combine(RepoRoot(), project);
        var projectDir = Path.GetDirectoryName(csproj)!;
        var sep = Path.DirectorySeparatorChar;

        // The project's own files (the SDK compiles every .cs under its folder) plus the files it links from src/Shared and src/Grpc.Shared.
        var files = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal))
            .ToList();
        foreach (Match m in Regex.Matches(File.ReadAllText(csproj), "<Compile Include=\"([^\"]+)\""))
        {
            var include = Path.GetFullPath(Path.Combine(projectDir, m.Groups[1].Value.Replace('\\', '/')));
            var pattern = Path.GetFileName(include);
            var dir = Path.GetDirectoryName(include)!;
            if (Directory.Exists(dir)) files.AddRange(Directory.GetFiles(dir, pattern));
        }

        var withLoggerMessage = files
            .Distinct(StringComparer.Ordinal)
            .Where(f => File.ReadAllText(f).Contains("[LoggerMessage(", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoRoot(), f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(withLoggerMessage.Length <= 1,
            $"{project} compiles {withLoggerMessage.Length} files with [LoggerMessage] methods: {string.Join(", ", withLoggerMessage)}. " +
            "Move them into one class in one file, or builds stop being reproducible.");
    }
}
