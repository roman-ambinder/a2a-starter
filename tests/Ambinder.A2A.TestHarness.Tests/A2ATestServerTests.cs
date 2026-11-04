using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;
using Ambinder.A2A.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.TestHarness.Tests;

public class A2ATestServerTests
{
    private sealed class EchoAgent : IAgentHandler
    {
        public ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
        {
            var text = string.Concat(context.Message.Parts.Select(p => p.Text));
            return sink.ReplyAsync(
                new Message { MessageId = "reply-1", Role = Role.Agent, Parts = [Part.FromText("You said: " + text)] }, ct);
        }
    }

    private static Task<A2ATestServer> StartEchoAsync()
        => A2ATestServer.StartAsync(a => a.AddAgent<EchoAgent>("echo", _ => new AgentCard { Name = "echo", Version = "1.0.0" }));

    [Fact]
    public async Task EchoOverJsonRpcAndRest()
    {
        await using var server = await StartEchoAsync();
        Assert.Equal([A2ABinding.JsonRpc, A2ABinding.Rest], A2ABindings.All);

        foreach (var binding in A2ABindings.All)
        {
            await using var client = server.CreateClient(binding);
            var response = await client.SendMessageAsync(new SendMessageRequest
            {
                Message = new Message { MessageId = $"m-{binding}", Role = Role.User, Parts = [Part.FromText("hi")] },
            }, TestContext.Current.CancellationToken);

            Assert.NotNull(response.Message);
            Assert.Equal("You said: hi", response.Message.Parts[0].Text);
        }
    }

    [Fact]
    public async Task TimeIsFake()
    {
        await using var server = await StartEchoAsync();

        Assert.Same(server.Time, server.Services.GetRequiredService<TimeProvider>());
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), server.Time.GetUtcNow());
    }
}
