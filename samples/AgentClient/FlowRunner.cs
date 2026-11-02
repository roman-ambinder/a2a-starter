using A2A.Client;
using A2A.Client.Auth;
using A2A.Protocol;
using Azure.Identity;

namespace AgentClient;

/// <summary>
/// One method per demo flow. A flow catches its own errors and reports them as a failed <see cref="FlowResult"/>; it never throws.
/// The flows are in <c>FlowRunner.Basics.cs</c> (discover, send, stream, get, list, cancel, resubscribe) and
/// <c>FlowRunner.Advanced.cs</c> (multi-turn, push, auth, extended-card, extensions, errors).
/// </summary>
public sealed partial class FlowRunner : IDisposable
{
    public const string Echo = "echo", Report = "report", Booking = "booking", Secure = "secure";
    public const string TimestampExtension = "https://example.com/a2a/ext/timestamp/v1";
    public const string AuditExtension = "https://example.com/a2a/ext/audit/v1";

    /// <summary>The flows in the order <see cref="RunAllAsync"/> runs them.</summary>
    public static IReadOnlyList<string> FlowNames { get; } =
        ["discover", "send", "stream", "get", "list", "cancel", "resubscribe", "multi-turn", "push", "auth", "extended-card", "extensions", "errors"];

    private readonly FlowOptions _options;
    private readonly TextWriter _output;
    private readonly HttpClient _http;
    private readonly A2ACardResolver _cards;
    private readonly List<IDisposable> _disposables = [];

    public FlowRunner(FlowOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _output = TextWriter.Synchronized(options.Output);
        var handler = new SocketsHttpHandler();
        _http = new HttpClient(options.Verbose ? new WireLogHandler(_output, handler) : handler) { Timeout = TimeSpan.FromSeconds(60) };
        _cards = new A2ACardResolver(_http);
    }

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
        _http.Dispose();
    }

    /// <summary>Runs every flow in order. A failing flow does not stop the others.</summary>
    public async Task<IReadOnlyList<FlowResult>> RunAllAsync(CancellationToken ct = default)
    {
        var results = new List<FlowResult>();
        foreach (var name in FlowNames) results.Add(await RunAsync(name, ct).ConfigureAwait(false));
        return results;
    }

    /// <summary>Runs one flow by its command name.</summary>
    public Task<FlowResult> RunAsync(string flow, CancellationToken ct = default) => flow switch
    {
        "discover" => DiscoverAsync(ct),
        "send" => SendAsync(ct),
        "stream" => StreamAsync(ct),
        "get" => GetAsync(ct),
        "list" => ListAsync(ct),
        "cancel" => CancelAsync(ct),
        "resubscribe" => ResubscribeAsync(ct),
        "multi-turn" => MultiTurnAsync(ct),
        "push" => PushAsync(ct),
        "auth" => AuthAsync(ct),
        "extended-card" => ExtendedCardAsync(ct),
        "extensions" => ExtensionsAsync(ct),
        "errors" => ErrorsAsync(ct),
        _ => throw new ArgumentException($"Unknown flow '{flow}'.", nameof(flow)),
    };

    // ---- plumbing ----

    /// <summary>Records steps of one flow and prints them as they happen.</summary>
    private sealed class Flow(string name, TextWriter output)
    {
        private readonly List<string> _steps = [];
        public string Name => name;
        public IReadOnlyList<string> Steps => _steps;

        public void Step(string text)
        {
            _steps.Add(text);
            output.WriteLine($"  - {text}");
        }

        /// <summary>Records the step; throws when <paramref name="ok"/> is false.</summary>
        public void Check(bool ok, string text)
        {
            if (!ok) throw new FlowException(text);
            Step(text);
        }

        public void Fail(string text)
        {
            _steps.Add("FAILED: " + text);
            output.WriteLine($"  ! FAILED: {text}");
        }
    }

    private sealed class FlowException(string message) : Exception(message);

    private async Task<FlowResult> RunFlowAsync(string name, Func<Flow, Task> body)
    {
        _output.WriteLine($"== {name} ==");
        var flow = new Flow(name, _output);
        var passed = true;
        try
        {
            await body(flow).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            passed = false;
            flow.Fail(Describe(e));
        }
        _output.WriteLine(passed ? $"[PASS] {name}" : $"[FAIL] {name}");
        _output.Flush();
        return new FlowResult(name, passed, flow.Steps);
    }

    private static string Describe(Exception e) => e switch
    {
        FlowException f => f.Message,
        A2AException a => $"A2AException {a.Code} ({(int)a.Code}): {a.Message}",
        A2AAuthException a => $"A2AAuthException HTTP {a.HttpStatus}: {a.Message}",
        _ => $"{e.GetType().Name}: {e.Message}",
    };

    private string AgentOr(string defaultAgent) => string.IsNullOrWhiteSpace(_options.Agent) ? defaultAgent : _options.Agent;

    private Uri AgentUri(string agent) => new($"{_options.ServerUrl.TrimEnd('/')}/agents/{agent}");

    private Task<AgentCard> CardAsync(string agent, CancellationToken ct) => _cards.GetAgentCardAsync(AgentUri(agent), ct);

    private IA2ACredentialProvider? Credentials(Identity who, bool anonymous = false, bool forceMock = false)
    {
        if (anonymous) return null;
        var mode = forceMock ? AuthMode.Mock : _options.Auth;
        switch (mode)
        {
            case AuthMode.None:
                return null;
            case AuthMode.ApiKey:
                return new ApiKeyCredentialProvider(_options.ApiKey);
            case AuthMode.Entra:
                return new AzureIdentityCredentialProvider(new DefaultAzureCredential(), _options.Scope);
            default:
                var secret = who switch
                {
                    Identity.Approver => _options.Approver,
                    Identity.NoRoles => _options.NoRoles,
                    _ => _options.Client,
                };
                // The echo card has no oauth2 scheme, so the token URL is always given.
                var provider = new ClientCredentialsProvider(secret.Id, secret.Secret, _options.Scope, new Uri(_options.TokenUrl), _http);
                _disposables.Add(provider);
                return provider;
        }
    }

    private async Task<IA2AClient> ClientAsync(string agent, Identity who = Identity.Client, IReadOnlyList<string>? extensions = null,
        bool anonymous = false, bool forceMock = false, string? protocolVersion = null, CancellationToken ct = default)
    {
        var card = await CardAsync(agent, ct).ConfigureAwait(false);
        return A2AClientFactory.Create(card, new A2AClientOptions
        {
            HttpClient = _http,
            Credentials = Credentials(who, anonymous, forceMock),
            PreferredBinding = _options.Binding,
            Extensions = extensions ?? [],
            ProtocolVersion = protocolVersion ?? A2AConstants.ProtocolVersion,
        });
    }

    private static SendMessageRequest Request(string text, string? taskId = null, string? contextId = null, bool returnImmediately = false,
        TaskPushNotificationConfig? push = null, params Part[] extra) => new()
    {
        Message = new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            TaskId = taskId,
            ContextId = contextId,
            Role = Role.User,
            Parts = [Part.FromText(text), .. extra],
        },
        Configuration = returnImmediately || push is not null
            ? new SendMessageConfiguration { ReturnImmediately = returnImmediately, TaskPushNotificationConfig = push }
            : null,
    };

    private static string TextOf(Message? message) => message is null ? "" : string.Concat(message.Parts.Select(p => p.Text));

    private static string TextOf(Artifact artifact) => string.Concat(artifact.Parts.Select(p => p.Text));

    private static string Short(string text, int max = 60)
    {
        var one = text.ReplaceLineEndings(" ");
        return one.Length <= max ? one : one[..max] + "...";
    }

    private static bool IsTerminal(TaskState state)
        => state is TaskState.Completed or TaskState.Failed or TaskState.Canceled or TaskState.Rejected;

    private static async Task<AgentTask> WaitForStateAsync(IA2AClient client, string taskId, Func<TaskState, bool> done, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            var task = await client.GetTaskAsync(new GetTaskRequest { Id = taskId }, timeout.Token).ConfigureAwait(false);
            if (done(task.Status.State)) return task;
            await Task.Delay(50, timeout.Token).ConfigureAwait(false);
        }
    }
}
