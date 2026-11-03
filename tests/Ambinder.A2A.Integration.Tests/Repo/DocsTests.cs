using System.Text.RegularExpressions;
using AgentClient.Commands;

namespace Ambinder.A2A.Integration.Tests.Repo;

/// <summary>The docs must not drift: the README flow map names every console command and flow test class, and links resolve.</summary>
public sealed class DocsTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ambinder.A2A.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Ambinder.A2A.slnx not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Every_console_command_and_flow_test_class_is_in_flow_map()
    {
        var root = RepoRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var start = readme.IndexOf("## Flow map", StringComparison.Ordinal);
        Assert.True(start >= 0, "README.md has no '## Flow map' section");
        var end = readme.IndexOf("\n## ", start + 3, StringComparison.Ordinal);
        var flowMap = end < 0 ? readme[start..] : readme[start..end];

        var commands = FlowCommands.Build(TextWriter.Null).Subcommands.Select(c => c.Name).ToList();
        Assert.NotEmpty(commands);
        foreach (var command in commands)
            Assert.Contains($"`{command}`", flowMap);

        var testClasses = Directory.GetFiles(Path.Combine(root, "tests", "Ambinder.A2A.Integration.Tests", "Flows"), "*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"^public sealed class (\w+)", RegexOptions.Multiline).Select(m => m.Groups[1].Value))
            .ToList();
        Assert.NotEmpty(testClasses);
        foreach (var testClass in testClasses)
            Assert.Contains(testClass, flowMap);
    }

    [Fact]
    public void Relative_links_resolve()
    {
        var root = RepoRoot();
        var files = new List<string> { Path.Combine(root, "README.md") };
        files.AddRange(Directory.GetFiles(Path.Combine(root, "docs"), "*.md"));
        foreach (var top in new[] { "src", "samples" })
            files.AddRange(Directory.GetFiles(Path.Combine(root, top), "README.md", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")));

        Assert.Contains(files, f => f.EndsWith(Path.Combine("docs", "flows.md")));
        Assert.Contains(files, f => f.EndsWith(Path.Combine("docs", "production.md")));
        foreach (var project in new[] { "Ambinder.A2A.Protocol", "Ambinder.A2A.Protocol.Grpc", "Ambinder.A2A.Server", "Ambinder.A2A.Client" })
            Assert.Contains(files, f => f.EndsWith(Path.Combine(project, "README.md")));

        var broken = new List<string>();
        foreach (var file in files)
        {
            var text = Regex.Replace(File.ReadAllText(file), @"```.*?```", "", RegexOptions.Singleline);
            foreach (Match m in Regex.Matches(text, @"\]\(([^)\s]+)\)"))
            {
                var target = m.Groups[1].Value;
                if (target.StartsWith('#') || Regex.IsMatch(target, @"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase)) continue;
                var path = target.Split('#')[0];
                var full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, path));
                if (!File.Exists(full) && !Directory.Exists(full)) broken.Add($"{Path.GetRelativePath(root, file)} -> {target}");
            }
        }
        Assert.Empty(broken);
    }
}
