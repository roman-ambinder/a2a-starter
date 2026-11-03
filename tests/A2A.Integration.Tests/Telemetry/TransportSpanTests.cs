using A2A.Client;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;
using A2A.Tests.Shared;

namespace A2A.Integration.Tests.Telemetry;

[Collection(A2ATestHostCollection.Name)]
public sealed class TransportSpanTests(A2ATestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("JSONRPC", "jsonrpc")]
    [InlineData("HTTP+JSON", "rest")]
    [InlineData("GRPC", "grpc")]
    public async Task Server_span_records_transport_for_each_binding(string binding, string expected)
    {
        // The HTTP client and ASP.NET Core sources carry the trace from the client to the server span (in process).
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource, "System.Net.Http", "Microsoft.AspNetCore");
        var card = await host.Cards.GetAgentCardAsync(new Uri($"{A2ATestHost.BaseUrl}/agents/echo"), Ct);
        await using var client = A2AClientFactory.Create(card, new A2AClientOptions { PreferredBinding = binding });

        await client.SendMessageAsync(new SendMessageRequest
        {
            Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText("hi")] },
        }, Ct);

        var span = await collector.WaitForAsync("a2a.server SendMessage", TimeSpan.FromSeconds(5));
        Assert.Equal(expected, span.GetTagItem(A2ATelemetryNames.Transport));
    }
}
