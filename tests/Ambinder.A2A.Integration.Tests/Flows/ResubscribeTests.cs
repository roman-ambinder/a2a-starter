using Ambinder.A2A.Client;
using Ambinder.A2A.Client.Auth;
using Ambinder.A2A.Integration.Tests.Fixtures;
using Ambinder.A2A.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.Integration.Tests.Flows;

/// <summary>
/// Review Focus 1: a client drops its stream mid-task, the task keeps running, SubscribeToTask reattaches.
/// The test must subscribe while the task is still running. Instead of racing the 20 ms step delay it uses its own AgentServer
/// (port base + 3000) whose chat client answers the first section, then waits on a gate. The test opens the gate only after
/// the subscription is attached, so the task cannot finish early. MockEntra is shared with the main test host.
/// </summary>
[Collection(A2ATestHostCollection.Name)]
public sealed class ResubscribeTests(A2ATestHost host) : IAsyncLifetime
{
    private static readonly int PortBase = A2ATestHost.PortBase + 3000;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly GatedChatClient _chat = new();
    private WebApplication? _server;

    public async ValueTask InitializeAsync()
        => _server = await A2ATestHost.StartAgentServerAsync(PortBase, A2ATestHost.MockEntraBaseUrl,
            b => b.Host.ConfigureServices((_, services) => services.AddSingleton<IChatClient>(_chat)));

    public async ValueTask DisposeAsync()
    {
        _chat.Open();
        if (_server is not null) await _server.DisposeAsync();
    }

    [Fact]
    public void Server_uses_the_gated_chat_client()
        => Assert.Same(_chat, _server!.Services.GetRequiredService<IChatClient>());

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Resubscribe_after_disconnect_receives_terminal_event(string binding)
    {
        var card = await host.Cards.GetAgentCardAsync(new Uri($"{A2ATestHost.AgentServerBaseUrl(PortBase)}/agents/report"), Ct);
        await using var client = A2AClientFactory.Create(card, new A2AClientOptions
        {
            HttpClient = host.Http,
            Credentials = new ApiKeyCredentialProvider(A2ATestHost.ApiKey),
            PreferredBinding = binding,
        });
        var request = new SendMessageRequest
        {
            Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText("Resubscribe topic")] },
        };

        // Start the stream, read up to the first artifact event, then drop the connection.
        string? taskId = null;
        var enumerator = client.SendStreamingMessageAsync(request, Ct).GetAsyncEnumerator(Ct);
        try
        {
            while (await enumerator.MoveNextAsync())
            {
                taskId ??= enumerator.Current.Task?.Id;
                if (enumerator.Current.ArtifactUpdate is not null) break;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
        Assert.NotNull(taskId);

        // The agent is parked on the gate: the task is still running after the disconnect.
        var running = await client.GetTaskAsync(new GetTaskRequest { Id = taskId }, Ct);
        Assert.Equal(TaskState.Working, running.Status.State);

        var events = new List<StreamResponse>();
        await foreach (var e in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = taskId }, Ct))
        {
            events.Add(e);
            if (events.Count == 1) _chat.Open();
        }

        var snapshot = Assert.IsType<AgentTask>(events[0].Task);
        Assert.Equal(taskId, snapshot.Id);
        Assert.Equal(TaskState.Working, snapshot.Status.State);
        Assert.Equal(TaskState.Completed, events[^1].StatusUpdate!.Status.State);

        var final = await client.GetTaskAsync(new GetTaskRequest { Id = taskId }, Ct);
        Assert.Equal(TaskState.Completed, final.Status.State);
        Assert.Equal(["report", "summary", "data"], final.Artifacts.Select(a => a.ArtifactId));
    }
}

/// <summary>Answers the first call; later calls wait for <see cref="Open"/>. One test per instance, so it opens once.</summary>
internal sealed class GatedChatClient : IChatClient
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;

    public void Open() => _gate.TrySetResult();

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _calls) > 1) await _gate.Task.WaitAsync(cancellationToken);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, "section"));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
