using System.Text.RegularExpressions;

namespace Ambinder.A2A.Integration.Tests.Repo;

/// <summary>docker-compose.yml keeps the documented port layout. Parsed as text; no YAML package.</summary>
public sealed class ComposeFileTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ambinder.A2A.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Ambinder.A2A.slnx not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Text of one top-level service block (from "  name:" to the next two-space key).</summary>
    private static string Service(string name)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "docker-compose.yml"));
        var start = Array.FindIndex(lines, l => l == $"  {name}:");
        Assert.True(start >= 0, $"service '{name}' not found");
        var block = lines.Skip(start + 1).TakeWhile(l => !Regex.IsMatch(l, @"^  \S") && !Regex.IsMatch(l, @"^\S"));
        return string.Join('\n', block);
    }

    private static string[] Ports(string service)
        => Regex.Matches(Service(service), @"^\s*-\s*""?(\d+:\d+)""?\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();

    [Fact]
    public void Compose_ports_match_documented_layout()
    {
        Assert.Equal(["5100:5100"], Ports("mock-entra"));
        Assert.Equal(["5300:5300"], Ports("webhook-receiver"));
        Assert.Equal(["5200:5200", "5201:5201", "5202:5202", "5203:5203", "5204:5204"], Ports("agent-server"));
    }

    [Theory]
    [InlineData("mock-entra")]
    [InlineData("webhook-receiver")]
    [InlineData("agent-server")]
    [InlineData("client")]
    public void Every_dotnet_service_sets_Development_in_compose(string service)
        => Assert.Matches(@"ASPNETCORE_ENVIRONMENT:\s*""?Development""?", Service(service));

    [Fact]
    public void No_Dockerfile_hard_codes_the_Development_environment()
    {
        var files = Directory.GetFiles(Path.Combine(RepoRoot(), "docker"), "Dockerfile*");
        Assert.NotEmpty(files);
        foreach (var file in files)
            Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT=Development", File.ReadAllText(file));
    }

    [Fact]
    public void Client_and_ollama_are_opt_in_profiles_and_servers_wait_for_health()
    {
        Assert.Matches(@"profiles:\s*\[\s*""?tools""?\s*\]", Service("client"));
        Assert.Matches(@"profiles:\s*\[\s*""?ollama""?\s*\]", Service("ollama"));
        Assert.Contains("condition: service_healthy", Service("agent-server"));
        Assert.Contains("/health/ready", Service("agent-server"));
    }
}
