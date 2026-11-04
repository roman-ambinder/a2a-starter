using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;
using Ambinder.A2A.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.TestHarness.Tests;

public class TaskEventLogTests
{
    private sealed class Gate
    {
        public TaskCompletionSource Open { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class GatedAgent(Gate gate) : IAgentHandler
    {
        public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            await gate.Open.Task.WaitAsync(ct);
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
        }
    }

    private static SendMessageRequest Send(string id) => new()
    {
        Message = new Message { MessageId = id, Role = Role.User, Parts = [Part.FromText("go")] },
        Configuration = new SendMessageConfiguration { ReturnImmediately = true },
    };

    [Fact]
    public async Task WaitForTaskStateCompletesOnEvent()
    {
        var gate = new Gate();
        await using var server = await A2ATestServer.StartAsync(
            a => a.AddAgent<GatedAgent>("gated", _ => new AgentCard { Name = "gated", Version = "1.0.0" }),
            s => s.AddSingleton(gate));
        await using var client = server.CreateClient();

        var response = await client.SendMessageAsync(Send("m1"), TestContext.Current.CancellationToken);
        var taskId = Assert.IsType<AgentTask>(response.Task).Id;

        var wait = server.Events.WaitForTaskStateAsync(taskId, TaskState.Completed, TimeSpan.FromSeconds(10));
        gate.Open.SetResult();
        var evt = await wait;

        Assert.Equal(taskId, evt.TaskId);
        Assert.Equal(TaskState.Completed, evt.State);
        Assert.Equal("gated", evt.AgentName);
    }

    [Fact]
    public async Task WaitTimesOutWithEventsInMessage()
    {
        var gate = new Gate();
        gate.Open.SetResult();
        await using var server = await A2ATestServer.StartAsync(
            a => a.AddAgent<GatedAgent>("gated", _ => new AgentCard { Name = "gated", Version = "1.0.0" }),
            s => s.AddSingleton(gate));
        await using var client = server.CreateClient();

        var response = await client.SendMessageAsync(Send("m2"), TestContext.Current.CancellationToken);
        var taskId = Assert.IsType<AgentTask>(response.Task).Id;
        await server.Events.WaitForTaskStateAsync(taskId, TaskState.Completed, TimeSpan.FromSeconds(10));

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => server.Events.WaitForAsync(_ => false, TimeSpan.FromMilliseconds(50)));

        Assert.Contains(taskId, ex.Message);
    }
}
