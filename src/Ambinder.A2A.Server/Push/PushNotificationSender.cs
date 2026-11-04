using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Tasks;
using Ambinder.A2A.Server.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Server.Push;

/// <summary>
/// Delivers task events to webhooks. As an <see cref="ITaskEventObserver"/> it only queues work (it runs under the task lock);
/// as a hosted service it does all the HTTP in the background. Events of one task are delivered in order; different tasks in parallel.
/// A failing webhook is retried (<see cref="PushOptions.MaxAttempts"/>, exponential backoff), then logged and given up on.
/// When a task is terminal, its configs are removed after the last delivery for that event finishes.
/// </summary>
internal sealed class PushNotificationSender : BackgroundService, ITaskEventObserver
{
    public const string HttpClientName = "a2a-push";
    private const int QueueCapacity = 10_000;

    private sealed class Lane
    {
        public readonly Queue<PushWorkItem> Items = new();
        public bool Running;
        public bool Closed;
    }

    private readonly Channel<PushWorkItem> _channel = Channel.CreateBounded<PushWorkItem>(new BoundedChannelOptions(QueueCapacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
    });
    private readonly ConcurrentDictionary<(string Agent, string TaskId), Lane> _lanes = new();
    private readonly ConcurrentDictionary<Task, byte> _drains = new();
    private readonly IHttpClientFactory _httpClients;
    private readonly IPushConfigStore _store;
    private readonly PushSigningKey _signingKey;
    private readonly PushOptions _options;
    private readonly PushAttemptAddressCheck _addressCheck;
    private readonly ILogger<PushNotificationSender> _logger;
    private readonly TimeProvider _time;

    public PushNotificationSender(IHttpClientFactory httpClients, IPushConfigStore store, PushSigningKey signingKey,
        IOptions<PushOptions> options, PushAttemptAddressCheck addressCheck, ILogger<PushNotificationSender> logger, TimeProvider? time = null)
    {
        _httpClients = httpClients;
        _store = store;
        _signingKey = signingKey;
        _options = options.Value;
        _addressCheck = addressCheck;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Queues the event. Never blocks and never throws; if the queue is full the event is dropped and logged.</summary>
    public ValueTask OnTaskEventAsync(StoredTask task, StreamResponse evt, CancellationToken ct)
    {
        var item = new PushWorkItem(task.AgentName, task.Task.Id, evt, TaskStateRules.IsTerminal(task.Task.Status.State),
            Activity.Current?.Context ?? default); // the delivery span joins the trace of whatever committed the event
        if (!_channel.Writer.TryWrite(item))
        {
            Log.PushQueueFull(_logger, item.TaskId);
            CountDelivery(item.AgentName, "dropped");
        }
        return ValueTask.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
                Dispatch(item, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(_drains.Keys).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Dispatch(PushWorkItem item, CancellationToken ct)
    {
        var key = (item.AgentName, item.TaskId);
        while (true)
        {
            var lane = _lanes.GetOrAdd(key, static _ => new Lane());
            lock (lane)
            {
                if (lane.Closed) continue; // the drain just finished and removed this lane; use a fresh one
                lane.Items.Enqueue(item);
                if (lane.Running) return;
                lane.Running = true;
            }
            var drain = Task.Run(() => DrainAsync(key, lane, ct), CancellationToken.None);
            _drains[drain] = 0;
            _ = drain.ContinueWith(t => _drains.TryRemove(t, out _), TaskScheduler.Default);
            return;
        }
    }

    private async Task DrainAsync((string Agent, string TaskId) key, Lane lane, CancellationToken ct)
    {
        while (true)
        {
            PushWorkItem? item;
            lock (lane)
            {
                if (!lane.Items.TryDequeue(out item))
                {
                    lane.Running = false;
                    lane.Closed = true;
                    _lanes.TryRemove(KeyValuePair.Create(key, lane));
                    return;
                }
            }
            try
            {
                await DeliverAsync(item, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Log.PushDeliveryFailedUnexpectedly(_logger, ex, item.TaskId);
            }
        }
    }

    private async Task DeliverAsync(PushWorkItem item, CancellationToken ct)
    {
        var configs = await _store.GetAllAsync(item.AgentName, item.TaskId, ct).ConfigureAwait(false);
        if (configs.Count > 0)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(item.Event, (JsonTypeInfo<StreamResponse>)A2AJson.Options.GetTypeInfo(typeof(StreamResponse)));
            await Task.WhenAll(configs.Select(c => DeliverToAsync(item, c, body, ct))).ConfigureAwait(false);
        }
        if (item.IsFinal)
            await _store.DeleteAllAsync(item.AgentName, item.TaskId, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task DeliverToAsync(PushWorkItem item, TaskPushNotificationConfig config, byte[] body, CancellationToken ct)
    {
        var target = Uri.TryCreate(config.Url, UriKind.Absolute, out var url) ? url : null;
        if (target is null) return;

        // The HttpClient call runs inside this span, so the request carries its traceparent.
        // Only the host is recorded: the rest of the URL may hold secrets.
        using var span = ServerInstruments.Source.StartActivity("a2a.push.deliver", ActivityKind.Internal, item.Parent);
        span?.SetTag(A2ATelemetryNames.Agent, item.AgentName);
        span?.SetTag(A2ATelemetryNames.TaskId, item.TaskId);
        span?.SetTag(A2ATelemetryNames.ServerAddress, target.Host);
        var outcome = await AttemptDeliveryAsync(item, config, target, body, span, ct).ConfigureAwait(false);
        span?.SetTag(A2ATelemetryNames.Outcome, outcome);
        if (outcome == "failed") span?.SetStatus(ActivityStatusCode.Error);
        if (outcome != "canceled") CountDelivery(item.AgentName, outcome);
    }

    /// <summary>Sends with retries. Returns "delivered", "failed" or "canceled" (shutdown, or the config was deleted); counts each retry.</summary>
    private async Task<string> AttemptDeliveryAsync(PushWorkItem item, TaskPushNotificationConfig config, Uri target, byte[] body, Activity? span, CancellationToken ct)
    {
        var where = target.GetLeftPart(UriPartial.Path); // no query string in logs: it may hold secrets
        var attempts = 1 + Math.Max(0, _options.MaxAttempts - 1); // the old sender counts retries; MaxAttempts includes the first attempt

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            span?.SetTag(A2ATelemetryNames.PushAttempt, attempt);
            string failure;
            try
            {
                using var request = BuildRequest(item, config, target, body);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(_options.Timeout);
                // Proxy mode only: the connect-time check cannot see the webhook address, so check it before every attempt.
                await _addressCheck.CheckAsync(target, timeout.Token).ConfigureAwait(false);
                using var response = await _httpClients.CreateClient(HttpClientName)
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return "delivered";
                failure = $"HTTP {(int)response.StatusCode}";
                var code = (int)response.StatusCode;
                if (code is >= 400 and < 500 && code is not (408 or 429))
                {
                    Log.PushRejected(_logger, where, item.TaskId, failure);
                    return "failed";
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return "canceled";
            }
            catch (Exception ex)
            {
                failure = ex is OperationCanceledException ? "timeout" : ex.GetType().Name + ": " + ex.Message;
            }

            if (attempt == attempts)
            {
                Log.PushGaveUp(_logger, where, item.TaskId, attempts, failure);
                return "failed";
            }

            CountDelivery(item.AgentName, "retried");
            var delay = _options.InitialBackoff * Math.Pow(2, attempt - 1);
            Log.PushRetrying(_logger, where, item.TaskId, failure, attempt, attempts - 1, delay);
            try { await Task.Delay(delay, _time, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return "canceled"; }

            // Stop retrying a config that was deleted in the meantime.
            if (await _store.GetAsync(item.AgentName, item.TaskId, config.Id ?? "", ct).ConfigureAwait(false) is null) return "canceled";
        }
        return "failed"; // not reached: the last attempt returns above
    }

    private static void CountDelivery(string agent, string outcome)
        => ServerInstruments.PushDeliveries.Add(1,
            new KeyValuePair<string, object?>(A2ATelemetryNames.Agent, agent),
            new KeyValuePair<string, object?>(A2ATelemetryNames.Outcome, outcome));

    private HttpRequestMessage BuildRequest(PushWorkItem item, TaskPushNotificationConfig config, Uri url, byte[] body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/a2a+json");
        if (config.Authentication is { Scheme.Length: > 0 } auth)
            request.Headers.TryAddWithoutValidation("Authorization", string.IsNullOrEmpty(auth.Credentials) ? auth.Scheme : $"{auth.Scheme} {auth.Credentials}");
        if (!string.IsNullOrEmpty(config.Token))
            request.Headers.TryAddWithoutValidation("X-A2A-Notification-Token", config.Token);
        request.Headers.TryAddWithoutValidation("X-A2A-Push-Signature",
            _signingKey.CreateToken(_options.Issuer, config.Url, item.TaskId, body, _time.GetUtcNow()));
        return request;
    }
}
