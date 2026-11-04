using Ambinder.A2A.Client.Tests.Fixtures;

namespace Ambinder.A2A.Client.Tests;

public sealed class SmokeTests
{
    [Fact]
    public async Task Project_builds_and_host_starts()
    {
        await using var host = await LoopbackAgentHost.StartAsync(agents => agents.AddEcho());
        using var http = new HttpClient();

        var json = await http.GetStringAsync(host.CardUrl("echo"), TestContext.Current.CancellationToken);

        Assert.Contains("\"echo\"", json);
        Assert.Contains($"{host.BaseUrl}/agents/echo/jsonrpc", json);
        Assert.Contains($"127.0.0.1:{host.GrpcPorts["echo"]}", json);
    }
}
