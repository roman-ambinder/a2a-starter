extern alias GettingStartedAgent;

using Ambinder.A2A.Client;
using Ambinder.A2A.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;

using AgentProgram = GettingStartedAgent::Program;

namespace Ambinder.A2A.Integration.Tests.Samples;

/// <summary>
/// The getting-started agent's own <c>Program</c> runs in memory (TestServer, so no port), and the same calls as
/// <c>samples/GettingStarted.Client/Program.cs</c> run against it. The client sample is a script of top-level statements, so the
/// calls are repeated here.
/// </summary>
public sealed class GettingStartedTests : IClassFixture<WebApplicationFactory<AgentProgram>>
{
    private readonly WebApplicationFactory<AgentProgram> _agent;

    public GettingStartedTests(WebApplicationFactory<AgentProgram> agent) => _agent = agent;

    [Fact]
    public async Task Client_gets_the_hello_reply_from_the_default_agent()
    {
        using var http = _agent.CreateClient();
        var card = await new A2ACardResolver(http).GetAgentCardAsync(new Uri("http://localhost:5100"), TestContext.Current.CancellationToken);
        var client = A2AClientFactory.Create(card, new A2AClientOptions { HttpClient = http });

        var reply = await client.SendMessageAsync(new SendMessageRequest
        {
            Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText("Ada")] },
        }, TestContext.Current.CancellationToken);

        Assert.Equal("hello", card.Name);
        Assert.NotNull(reply.Message);
        Assert.Equal("Hello, Ada!", Assert.Single(reply.Message.Parts).Text);
    }
}
