using System.Diagnostics;
using A2A.Client;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;
using A2A.Tests.Shared;

namespace A2A.Integration.Tests.Telemetry;

[Collection(A2ATestHostCollection.Name)]
public sealed class ClientSpanTests(A2ATestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("JSONRPC", "jsonrpc")]
    [InlineData("HTTP+JSON", "rest")]
    [InlineData("GRPC", "grpc")]
    public async Task Client_span_is_parent_of_server_span_on_every_transport(string binding, string expected)
    {
        // The HTTP, gRPC client and ASP.NET Core sources are the hops between the two spans.
        using var collector = new ActivityCollector(A2ATelemetryNames.ClientSource, A2ATelemetryNames.ServerSource,
            "System.Net.Http", "Grpc.Net.Client", "Microsoft.AspNetCore");
        await using var client = await host.CreateClientAsync("echo", binding, ct: Ct);

        await client.SendMessageAsync(new SendMessageRequest
        {
            Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText("hi")] },
        }, Ct);

        var clientSpan = await collector.WaitForAsync("a2a.client SendMessage", TimeSpan.FromSeconds(5));
        var serverSpan = await collector.WaitForAsync("a2a.server SendMessage", TimeSpan.FromSeconds(5));
        Assert.Equal(expected, clientSpan.GetTagItem(A2ATelemetryNames.Transport));
        Assert.Equal("SendMessage", clientSpan.GetTagItem(A2ATelemetryNames.Method));
        Assert.Equal("echo", clientSpan.GetTagItem(A2ATelemetryNames.Agent));
        Assert.Equal(clientSpan.TraceId, serverSpan.TraceId);
        Assert.True(IsAncestor(collector, clientSpan, serverSpan), "The client span should be an ancestor of the server span.");
    }

    [Fact]
    public async Task Client_error_sets_status_and_error_type()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ClientSource);
        await using var client = await host.CreateClientAsync("echo", "JSONRPC", ct: Ct);

        await Assert.ThrowsAsync<A2AException>(() => client.GetTaskAsync(new GetTaskRequest { Id = "no-such-task" }, Ct));

        var span = await collector.WaitForAsync("a2a.client GetTask", TimeSpan.FromSeconds(5));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("TaskNotFound", span.GetTagItem(A2ATelemetryNames.ErrorType));
        Assert.Equal("no-such-task", span.GetTagItem(A2ATelemetryNames.TaskId));
        Assert.Equal("echo", span.GetTagItem(A2ATelemetryNames.Agent));
    }

    [Fact]
    public async Task Client_stream_span_stays_open_over_the_enumeration()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ClientSource);
        await using var client = await host.CreateClientAsync("echo", "JSONRPC", ct: Ct);

        var count = 0;
        await foreach (var _ in client.SendStreamingMessageAsync(new SendMessageRequest
        {
            Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText("hi")] },
        }, Ct))
        {
            count++;
            Assert.DoesNotContain(collector.Stopped, a => a.OperationName == "a2a.client SendStreamingMessage");
        }

        Assert.True(count > 0);
        var span = await collector.WaitForAsync("a2a.client SendStreamingMessage", TimeSpan.FromSeconds(5));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    private static bool IsAncestor(ActivityCollector collector, Activity ancestor, Activity descendant)
    {
        var byId = collector.Stopped.ToDictionary(a => a.SpanId);
        for (var parent = descendant.ParentSpanId; byId.TryGetValue(parent, out var a); parent = a.ParentSpanId)
            if (a.SpanId == ancestor.SpanId) return true;
        return false;
    }
}
