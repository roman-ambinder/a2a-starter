using System.Diagnostics;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Telemetry;
using Ambinder.A2A.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.Server.Tests.Telemetry;

[Collection("Telemetry")]
public sealed class TracingMiddlewareTests
{
    [Fact]
    public async Task Streaming_operation_without_events_fails_and_ends_the_span()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        var context = new A2AOperationContext(A2AOperation.SendStreamingMessage, TestHost.Call(), TestHost.Text("hi"),
            new ServiceCollection().BuildServiceProvider(), new A2AResponseMetadata(),
            TestContext.Current.CancellationToken);

        await new TracingMiddleware().InvokeAsync(context, _ => ValueTask.FromResult(A2AOperationResult.FromValue(null)));

        var span = collector.Single("a2a.server SendStreamingMessage");
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, span.GetTagItem(A2ATelemetryNames.ErrorType));
    }
}
