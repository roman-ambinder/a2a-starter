using Ambinder.A2A.Client.Tests.TestDoubles;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;
using Ambinder.A2A.Server.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.Client.Tests.Fixtures;

/// <summary>Registration helpers for the in-test agents.</summary>
public static class ScriptedAgents
{
    /// <summary>A card with streaming on and a skill, named <paramref name="name"/>.</summary>
    public static AgentCard Card(string name) => new()
    {
        Name = name,
        Description = $"Test agent {name}",
        Version = "1.0.0",
        Capabilities = new AgentCapabilities { Streaming = true },
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
        Skills = [new AgentSkill { Id = "test", Name = "Test", Description = "A test skill." }],
    };

    public static A2AServerBuilder AddEcho(this A2AServerBuilder builder, string name = "echo")
        => builder.AddAgent<EchoAgent>(name, _ => Card(name));

    public static A2AServerBuilder AddBooking(this A2AServerBuilder builder, string name = "booking")
        => builder.AddAgent<BookingAgent>(name, _ => Card(name));

    /// <summary>Registers an agent that is built by the factory (so it can take constructor arguments).</summary>
    public static A2AServerBuilder AddScripted<THandler>(this A2AServerBuilder builder, string name, Func<IServiceProvider, THandler> factory)
        where THandler : class, IAgentHandler
    {
        builder.Services.AddSingleton(factory);
        return builder.AddAgent<THandler>(name, _ => Card(name));
    }
}

/// <summary>WORKING, then the artifact "echo: {text}", then COMPLETED.</summary>
public sealed class EchoAgent : IAgentHandler
{
    public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        var text = string.Concat(context.Message.Parts.Select(p => p.Text));
        await sink.UpdateStatusAsync(TaskState.Working, null, ct);
        await sink.AddArtifactAsync(new Artifact { ArtifactId = "echo", Parts = [Part.FromText($"echo: {text}")] }, append: false, lastChunk: true, ct);
        await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
    }
}

/// <summary>Works for <paramref name="duration"/> (measured by <paramref name="time"/>), then completes.</summary>
public sealed class SlowAgent(TimeSpan duration, TimeProvider? time = null) : IAgentHandler
{
    public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        await sink.UpdateStatusAsync(TaskState.Working, null, ct);
        await Task.Delay(duration, time ?? TimeProvider.System, ct);
        await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
    }
}

/// <summary>Plays the given actions against the sink, in order.</summary>
public sealed class StreamingAgent(IReadOnlyList<Func<IAgentEventSink, CancellationToken, ValueTask>> events) : IAgentHandler
{
    public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        foreach (var step in events) await step(sink, ct);
    }
}

/// <summary>Asks for input first. The continuation (a message with the task) completes the task.</summary>
public sealed class BookingAgent : IAgentHandler
{
    public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        if (context.CurrentTask is null)
        {
            await sink.UpdateStatusAsync(TaskState.InputRequired, Msg.Agent("Which date?"), ct);
            return;
        }
        await sink.UpdateStatusAsync(TaskState.Working, null, ct);
        await sink.AddArtifactAsync(new Artifact { ArtifactId = "booking", Parts = [Part.FromText("booked")] }, append: false, lastChunk: true, ct);
        await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
    }
}

/// <summary>
/// Sends <paramref name="events"/> working updates, then asks the host to abort every in-flight request, so a streaming client
/// sees the connection break. Then it waits for its own cancellation.
/// </summary>
public sealed class DropAfter(int events, Func<LoopbackAgentHost> host) : IAgentHandler
{
    public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        await sink.UpdateStatusAsync(TaskState.Working, null, ct);
        for (var i = 1; i < events; i++)
            await sink.AddArtifactAsync(new Artifact { ArtifactId = $"a{i}", Parts = [Part.FromText($"chunk {i}")] }, append: false, lastChunk: true, ct);
        host().AbortInFlightRequests();
        await Task.Delay(Timeout.Infinite, ct);
    }
}
