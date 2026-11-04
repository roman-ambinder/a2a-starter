using System.Runtime.CompilerServices;
using Ambinder.A2A.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ambinder.A2A.Client.Tests.TestDoubles;

/// <summary>
/// A transport factory for a made-up binding (<c>TEST</c>) and version (<c>9.9</c>). It records every request and replies from a
/// script, so tests can show that the client gives a foreign transport interceptors, auth and tracing.
/// </summary>
public sealed class ScriptedTransportFactory : IA2ATransportFactory
{
    public const string Binding = "TEST";
    public const string Version = "9.9";

    private readonly object _gate = new();
    private readonly List<A2ATransportRequest> _requests = [];

    /// <summary>Gets or sets the reply to a unary call. The default returns an empty <see cref="SendMessageResponse"/>.</summary>
    public Func<A2ATransportRequest, A2ATransportResponse> Reply { get; set; } = request => new A2ATransportResponse
    {
        Body = new SendMessageResponse(),
        Info = new A2AResponseInfo { Transport = Binding, ProtocolVersion = request.ProtocolVersion, Attempts = 1 },
    };

    /// <summary>Gets or sets the events a streaming call yields.</summary>
    public IReadOnlyList<StreamResponse> StreamEvents { get; set; } = [];

    public IReadOnlyList<string> Bindings { get; } = [Binding];

    public IReadOnlyList<string> ProtocolVersions { get; } = [Version];

    /// <summary>Gets a copy of the requests recorded so far.</summary>
    public IReadOnlyList<A2ATransportRequest> Requests
    {
        get { lock (_gate) return [.. _requests]; }
    }

    public List<A2ATransportContext> Contexts { get; } = [];

    public int Created { get; private set; }

    public int Disposed { get; private set; }

    public IA2ATransport Create(AgentInterface selected, AgentCard card, A2ATransportContext context)
    {
        lock (_gate)
        {
            Created++;
            Contexts.Add(context);
        }

        return new Transport(this);
    }

    /// <summary>Builds a context with defaults for tests.</summary>
    public static A2ATransportContext NewContext(HttpClient httpClient) => new()
    {
        ClientName = "default",
        HttpClient = httpClient,
        HandlerFactory = null,
        Limits = new A2AClientLimits(),
        Timeouts = new A2AClientTimeouts(),
        Logger = NullLogger.Instance,
        Time = TimeProvider.System,
    };

    private void Record(A2ATransportRequest request)
    {
        lock (_gate)
        {
            _requests.Add(request);
        }
    }

    private sealed class Transport(ScriptedTransportFactory owner) : IA2ATransport
    {
        public ValueTask<A2ATransportResponse> SendAsync(A2ATransportRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            owner.Record(request);
            var response = owner.Reply(request);
            request.OnHeaders?.Invoke(response.Info);
            return new ValueTask<A2ATransportResponse>(response);
        }

        public async IAsyncEnumerable<StreamResponse> StreamAsync(A2ATransportRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            owner.Record(request);
            request.OnHeaders?.Invoke(new A2AResponseInfo { Transport = Binding, ProtocolVersion = request.ProtocolVersion, Attempts = 1 });
            foreach (var e in owner.StreamEvents)
            {
                ct.ThrowIfCancellationRequested();
                yield return e;
            }

            await Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            owner.Disposed++;
            return ValueTask.CompletedTask;
        }
    }
}
