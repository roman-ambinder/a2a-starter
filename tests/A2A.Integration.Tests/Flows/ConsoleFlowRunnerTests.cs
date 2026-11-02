using System.Text.RegularExpressions;
using A2A.Integration.Tests.Fixtures;
using AgentClient;

namespace A2A.Integration.Tests.Flows;

/// <summary>Runs the console client's flows (samples/AgentClient) against the test host.</summary>
[Collection(A2ATestHostCollection.Name)]
public sealed class ConsoleFlowRunnerTests(A2ATestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly A2ATestHost _host = host;

    private static FlowOptions Options(string? binding, StringWriter output, bool verbose = false, AuthMode auth = AuthMode.Mock) => new()
    {
        ServerUrl = A2ATestHost.BaseUrl,
        Binding = binding,
        Auth = auth,
        Verbose = verbose,
        ReceiverUrl = A2ATestHost.ReceiverBaseUrl,
        MockEntraUrl = A2ATestHost.MockEntraBaseUrl,
        Output = output,
    };

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task All_flows_pass_against_test_host(string binding)
    {
        var output = new StringWriter();
        using var runner = new FlowRunner(Options(binding, output));

        var results = await runner.RunAllAsync(Ct);

        var failed = results.Where(r => !r.Passed).Select(r => $"{r.Flow}: {string.Join(" | ", r.Steps)}").ToList();
        Assert.True(failed.Count == 0, string.Join(Environment.NewLine, failed) + Environment.NewLine + output);
        Assert.Equal(FlowRunner.FlowNames, results.Select(r => r.Flow));
        Assert.All(results, r => Assert.NotEmpty(r.Steps));
    }

    [Theory]
    [InlineData("JSONRPC", AuthMode.Mock)]
    [InlineData("GRPC", AuthMode.Mock)]
    [InlineData("HTTP+JSON", AuthMode.ApiKey)]
    public async Task Verbose_log_masks_secrets(string binding, AuthMode auth)
    {
        var output = new StringWriter();
        using var runner = new FlowRunner(Options(binding, output, verbose: true, auth: auth));

        var results = auth == AuthMode.Mock
            ? [await runner.AuthAsync(Ct), await runner.PushAsync(Ct), await runner.SendAsync(Ct)]
            : new[] { await runner.SendAsync(Ct) };

        Assert.All(results, r => Assert.True(r.Passed, string.Join(" | ", r.Steps)));
        var text = output.ToString();
        Assert.Contains(auth == AuthMode.Mock ? "Authorization" : "X-API-Key", text);
        Assert.Contains("***", text);
        Assert.DoesNotContain("dev-client-secret", text);
        Assert.DoesNotContain("dev-approver-secret", text);
        Assert.DoesNotContain("dev-noroles-secret", text);
        Assert.DoesNotContain("dev-api-key", text);
        Assert.DoesNotMatch(new Regex("eyJ[A-Za-z0-9_-]{10,}"), text); // no JWT anywhere
        Assert.DoesNotMatch(new Regex("\"(credentials|token|access_token)\"\\s*:\\s*\"(?!\\*\\*\\*)"), text);
    }
}
