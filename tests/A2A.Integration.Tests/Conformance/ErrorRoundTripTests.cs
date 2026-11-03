using A2A.Client;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;
using A2A.Server;
using A2A.Server.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace A2A.Integration.Tests.Conformance;

/// <summary>
/// Design D4: every A2A error (catalog rows with an ErrorInfo reason) leaves the server on each transport and the client maps it
/// back to the same <see cref="A2AErrorCode"/>. A handler that throws the code named in the task id stands in for the agents.
/// </summary>
[Collection(A2ATestHostCollection.Name)]
public sealed class ErrorRoundTripTests(A2ATestHost host)
{
    /// <summary>Port base of the extra AgentServer this test starts.</summary>
    private static readonly int ExtraPortBase = A2ATestHost.PortBase + 7000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The catalog errors that carry an ErrorInfo reason (-32001..-32009).</summary>
    public static IEnumerable<A2AErrorCode> A2AErrors
        => Enum.GetValues<A2AErrorCode>().Where(c => A2AError.Get(c).Reason is not null);

    [Fact]
    public void Catalog_has_nine_a2a_errors() => Assert.Equal(9, A2AErrors.Count());

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Every_a2a_error_maps_back_to_the_same_code(string binding)
    {
        await using var server = await A2ATestHost.StartAgentServerAsync(ExtraPortBase, A2ATestHost.MockEntraBaseUrl,
            b => b.Host.ConfigureServices((_, services) => services.AddSingleton<IA2ARequestHandler>(sp => new ThrowingHandler(sp))));
        var card = await host.Cards.GetAgentCardAsync(new Uri($"{A2ATestHost.AgentServerBaseUrl(ExtraPortBase)}/agents/echo"), Ct);
        await using var client = A2AClientFactory.Create(card, new A2AClientOptions
        {
            HttpClient = host.Http,
            Credentials = host.Credentials(TestAuth.Client, card),
            PreferredBinding = binding,
        });

        foreach (var code in A2AErrors)
        {
            var error = await Assert.ThrowsAsync<A2AException>(() => client.GetTaskAsync(new GetTaskRequest { Id = code.ToString() }, Ct));
            Assert.Equal(code, error.Code);
            Assert.Contains("thrown for the test", error.Message, StringComparison.Ordinal);
            Assert.Equal("v", error.Details["k"]);
            Assert.Equal("1,2", error.Details["list"]);
        }
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Unknown_exception_becomes_internal_error_with_trace_id(string binding)
    {
        await using var server = await A2ATestHost.StartAgentServerAsync(ExtraPortBase + 10, A2ATestHost.MockEntraBaseUrl,
            b => b.Host.ConfigureServices((_, services) => services.AddSingleton<IA2ARequestHandler>(sp => new ThrowingHandler(sp))));
        var card = await host.Cards.GetAgentCardAsync(new Uri($"{A2ATestHost.AgentServerBaseUrl(ExtraPortBase + 10)}/agents/echo"), Ct);
        await using var client = A2AClientFactory.Create(card, new A2AClientOptions
        {
            HttpClient = host.Http,
            Credentials = host.Credentials(TestAuth.Client, card),
            PreferredBinding = binding,
        });

        var error = await Assert.ThrowsAsync<A2AException>(() => client.GetTaskAsync(new GetTaskRequest { Id = ThrowingHandler.CrashId }, Ct));

        Assert.Equal(A2AErrorCode.InternalError, error.Code);
        var traceId = error.Details["traceId"];
        Assert.Matches("^[0-9a-f]{32}$", traceId);
        Assert.StartsWith($"Internal error. Quote trace id {traceId} when you report it.", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret detail", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Delegates everything to the real handler except GetTask, which throws the code its id names.</summary>
    private sealed class ThrowingHandler(IServiceProvider services) : IA2ARequestHandler
    {
        private readonly TaskManager _inner = services.GetRequiredService<TaskManager>();

        public const string CrashId = "crash";

        public Task<AgentTask> GetTaskAsync(A2ACallContext call, GetTaskRequest request, CancellationToken ct)
            => request.Id == CrashId
                ? throw new InvalidOperationException("secret detail")
                : throw new A2AException(Enum.Parse<A2AErrorCode>(request.Id), "thrown for the test",
                    System.Text.Json.JsonSerializer.SerializeToElement(new { k = "v", list = new[] { 1, 2 } }));

        public Task<SendMessageResponse> SendMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct)
            => _inner.SendMessageAsync(call, request, ct);
        public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct)
            => _inner.SendStreamingMessageAsync(call, request, ct);
        public Task<ListTasksResponse> ListTasksAsync(A2ACallContext call, ListTasksRequest request, CancellationToken ct)
            => _inner.ListTasksAsync(call, request, ct);
        public Task<AgentTask> CancelTaskAsync(A2ACallContext call, CancelTaskRequest request, CancellationToken ct)
            => _inner.CancelTaskAsync(call, request, ct);
        public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(A2ACallContext call, SubscribeToTaskRequest request, CancellationToken ct)
            => _inner.SubscribeToTaskAsync(call, request, ct);
        public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(A2ACallContext call, TaskPushNotificationConfig request, CancellationToken ct)
            => _inner.CreateTaskPushNotificationConfigAsync(call, request, ct);
        public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(A2ACallContext call, GetTaskPushNotificationConfigRequest request, CancellationToken ct)
            => _inner.GetTaskPushNotificationConfigAsync(call, request, ct);
        public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(A2ACallContext call, ListTaskPushNotificationConfigsRequest request, CancellationToken ct)
            => _inner.ListTaskPushNotificationConfigsAsync(call, request, ct);
        public Task DeleteTaskPushNotificationConfigAsync(A2ACallContext call, DeleteTaskPushNotificationConfigRequest request, CancellationToken ct)
            => _inner.DeleteTaskPushNotificationConfigAsync(call, request, ct);
        public Task<AgentCard> GetExtendedAgentCardAsync(A2ACallContext call, GetExtendedAgentCardRequest request, CancellationToken ct)
            => _inner.GetExtendedAgentCardAsync(call, request, ct);
    }
}
