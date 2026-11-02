using System.CommandLine;
using System.Text.RegularExpressions;
using A2A.Integration.Tests.Fixtures;
using AgentClient;
using AgentClient.Commands;
using Microsoft.AspNetCore.Builder;

namespace A2A.Integration.Tests.Flows;

/// <summary>
/// Runs the console client's flows (samples/AgentClient). They run against a dedicated AgentServer (port base + 4000) with the
/// normal 500 ms agent step delay, so a report is still running when cancel and resubscribe act on it and the success paths run.
/// It has its own WebhookReceiver (base + 4300) that trusts it. MockEntra is shared with the main test host.
/// </summary>
[Collection(A2ATestHostCollection.Name)]
public sealed class ConsoleFlowRunnerTests(A2ATestHost host) : IAsyncLifetime
{
    private static readonly int PortBase = A2ATestHost.PortBase + 4000;
    private static readonly string ServerUrl = A2ATestHost.AgentServerBaseUrl(PortBase);
    private static readonly string ReceiverUrl = $"http://127.0.0.1:{PortBase + 300}";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly A2ATestHost _host = host;
    private WebApplication? _server;
    private WebApplication? _receiver;

    public async ValueTask InitializeAsync()
    {
        _receiver = await A2ATestHost.StartReceiverAsync(PortBase + 300, [ServerUrl]);
        _server = await A2ATestHost.StartAgentServerAsync(PortBase, A2ATestHost.MockEntraBaseUrl,
            b => b.Configuration["FakeAgents:StepDelay"] = TimeSpan.FromMilliseconds(500).ToString());
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null) await _server.DisposeAsync();
        if (_receiver is not null) await _receiver.DisposeAsync();
    }

    private static FlowOptions Options(string? binding, StringWriter output, bool verbose = false, AuthMode auth = AuthMode.Mock) => new()
    {
        ServerUrl = ServerUrl,
        Binding = binding,
        Auth = auth,
        Verbose = verbose,
        ReceiverUrl = ReceiverUrl,
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

        // The success paths ran: the task was canceled / the subscription reached COMPLETED, nothing was waved through as "already finished".
        var cancel = results.Single(r => r.Flow == "cancel").Steps;
        Assert.Contains("CancelTask returned Canceled", cancel);
        Assert.Contains("GetTask confirms Canceled", cancel);
        var resubscribe = results.Single(r => r.Flow == "resubscribe").Steps;
        Assert.Contains(resubscribe, s => s.StartsWith("SubscribeToTask delivered"));
        Assert.Contains("the subscription ended with the task completed", resubscribe);
        Assert.DoesNotContain(results.SelectMany(r => r.Steps), s => s.Contains("already finished"));
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
        if (auth == AuthMode.Mock) Assert.Contains("client_secret=***", text);
        Assert.DoesNotContain("dev-client-secret", text);
        Assert.DoesNotContain("dev-approver-secret", text);
        Assert.DoesNotContain("dev-noroles-secret", text);
        Assert.DoesNotContain("dev-api-key", text);
        Assert.DoesNotMatch(new Regex("eyJ[A-Za-z0-9_-]{10,}"), text); // no JWT anywhere
        Assert.DoesNotMatch(new Regex("\"(credentials|token|access_token)\"\\s*:\\s*\"(?!\\*\\*\\*)"), text);
    }

    private async Task<int> InvokeAsync(params string[] args)
    {
        var root = FlowCommands.Build(new StringWriter(), o => o with { MockEntraUrl = A2ATestHost.MockEntraBaseUrl, ReceiverUrl = ReceiverUrl });
        return await root.Parse(args).InvokeAsync(cancellationToken: Ct);
    }

    [Fact]
    public async Task Command_exit_code_is_0_when_the_flow_passes()
        => Assert.Equal(0, await InvokeAsync("extended-card", "--server", ServerUrl, "--transport", "jsonrpc"));

    [Fact]
    public async Task Command_exit_code_is_1_when_the_flow_fails()
        => Assert.Equal(1, await InvokeAsync("send", "--server", "http://127.0.0.1:1", "--transport", "jsonrpc"));

    [Fact]
    public async Task Command_exit_code_is_2_for_a_bad_option_value()
        => Assert.Equal(2, await InvokeAsync("send", "--server", ServerUrl, "--auth", "bogus"));
}
