using AgentClient;
using Ambinder.A2A.Integration.Tests.Fixtures;
using Ambinder.A2A.Protocol;
using System.Diagnostics;
using Ambinder.A2A.Tests.Shared;

namespace Ambinder.A2A.Integration.Tests.Telemetry;

[Collection(A2ATestHostCollection.Name)]
public sealed class FlowTelemetryTests(A2ATestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly A2ATestHost _host = host;

    [Fact]
    public async Task Each_flow_is_one_trace()
    {
        using var collector = new ActivityCollector(ClientTelemetry.SourceName, A2ATelemetryNames.ClientSource, A2ATelemetryNames.ServerSource,
            "System.Net.Http", "Microsoft.AspNetCore");
        var output = new StringWriter();
        using var runner = new FlowRunner(new FlowOptions
        {
            ServerUrl = A2ATestHost.BaseUrl,
            Binding = "JSONRPC",
            Auth = AuthMode.Mock,
            ReceiverUrl = A2ATestHost.ReceiverBaseUrl,
            MockEntraUrl = A2ATestHost.MockEntraBaseUrl,
            Output = output,
        });

        var result = await runner.RunAsync("send", Ct);

        Assert.True(result.Passed, output.ToString());
        var flow = collector.Single("flow send");
        Assert.Equal(collector.Root.SpanId, flow.ParentSpanId);
        var client = await collector.WaitForAsync("a2a.client SendMessage", TimeSpan.FromSeconds(5));
        var server = await collector.WaitForAsync("a2a.server SendMessage", TimeSpan.FromSeconds(5));
        Assert.Equal(flow.TraceId, client.TraceId);
        Assert.Equal(flow.TraceId, server.TraceId);
    }

    [Fact]
    public async Task Failed_flow_span_has_error_status_without_message_text()
    {
        using var collector = new ActivityCollector(ClientTelemetry.SourceName);
        var output = new StringWriter();
        using var runner = new FlowRunner(new FlowOptions
        {
            ServerUrl = A2ATestHost.BaseUrl,
            Agent = "no-such-agent",
            Binding = "JSONRPC",
            Auth = AuthMode.Mock,
            ReceiverUrl = A2ATestHost.ReceiverBaseUrl,
            MockEntraUrl = A2ATestHost.MockEntraBaseUrl,
            Output = output,
        });

        var result = await runner.RunAsync("send", Ct);

        Assert.False(result.Passed);
        var failure = result.Steps.Single(s => s.StartsWith("FAILED: ", StringComparison.Ordinal))["FAILED: ".Length..];
        var span = collector.Single("flow send");
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.NotNull(span.StatusDescription);
        Assert.DoesNotContain("no-such-agent", span.StatusDescription);
        Assert.DoesNotContain(failure, span.StatusDescription);
        Assert.NotNull(span.GetTagItem(A2ATelemetryNames.ErrorType));
    }
}
