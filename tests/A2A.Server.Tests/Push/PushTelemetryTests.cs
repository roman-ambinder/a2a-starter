using System.Diagnostics;
using System.Net;
using A2A.Protocol;
using A2A.Server.Push;
using A2A.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace A2A.Server.Tests.Push;

[Collection("Telemetry")]
public sealed class PushTelemetryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Stands in for the network: counts requests and answers with a fixed status.</summary>
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        private int _count;
        public int Count => _count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private static async Task<(IReadOnlyList<Activity> Spans, IReadOnlyList<(string Instrument, long Value, IReadOnlyDictionary<string, object?> Tags)> Metrics, TaskPushNotificationConfig Config)> RunAsync(
        HttpStatusCode status, string url, int maxRetries = 2, string? token = "tok-123", string? credentials = "secret-cred")
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        using var metrics = new MetricCollector(A2ATelemetryNames.ServerMeter);
        var handler = new StubHandler(status);
        var services = new ServiceCollection().AddLogging();
        services.AddA2APush(o =>
        {
            o.InitialBackoff = TimeSpan.FromMilliseconds(10);
            o.MaxRetries = maxRetries;
            o.AllowPrivateNetworks = true;
            o.Issuer = "https://agents.example.test";
        });
        services.AddHttpClient("a2a-push").ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<PushNotificationSender>();
        var configs = provider.GetRequiredService<IPushConfigStore>();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PushOptions>>().Value;
        var card = TestHost.DefaultCard with { Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = true } };
        var host = new TestHost(card, observers: [sender], pushConfigs: configs, pushOptions: options);
        await sender.StartAsync(CancellationToken.None);

        var config = new TaskPushNotificationConfig
        {
            Url = url,
            Token = token,
            Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = credentials },
        };
        // The task stays working, so its configs stay and every retry runs.
        var gate = new Gate();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            await gate.WaitAsync(ct);
        };
        await host.SendAsync(new SendMessageRequest
        {
            Message = TestHost.UserText("go"),
            Configuration = new SendMessageConfiguration { TaskPushNotificationConfig = config, ReturnImmediately = true },
        });
        // Stop only after both deliveries ended: StopAsync cancels the sender, which would turn a running delivery into "canceled".
        // A span stops after its metrics are counted, so the metrics are complete once both spans are there.
        await collector.WaitForCountAsync("a2a.push.deliver", 2, TestHost.Timeout);
        await sender.StopAsync(CancellationToken.None);
        gate.Open();

        // Two events are delivered: the new task, then the working status.
        var spans = collector.Stopped.Where(a => a.OperationName == "a2a.push.deliver").ToList();
        return (spans, metrics.Measurements, config);
    }


    private static long Count(IEnumerable<(string Instrument, long Value, IReadOnlyDictionary<string, object?> Tags)> m, string outcome)
        => m.Where(x => x.Instrument == A2ATelemetryNames.PushDeliveries && (string?)x.Tags[A2ATelemetryNames.Outcome] == outcome).Sum(x => x.Value);

    [Fact]
    public async Task Push_span_joins_task_trace()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ServerSource);
        var handler = new StubHandler(HttpStatusCode.OK);
        var services = new ServiceCollection().AddLogging();
        services.AddA2APush(o => { o.AllowPrivateNetworks = true; o.Issuer = "https://agents.example.test"; });
        services.AddHttpClient("a2a-push").ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<PushNotificationSender>();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PushOptions>>().Value;
        var card = TestHost.DefaultCard with { Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = true } };
        var host = new TestHost(card, observers: [sender], pushConfigs: provider.GetRequiredService<IPushConfigStore>(), pushOptions: options);
        await sender.StartAsync(CancellationToken.None);

        var response = await host.SendAsync(new SendMessageRequest
        {
            Message = TestHost.UserText("go"),
            Configuration = new SendMessageConfiguration { TaskPushNotificationConfig = new TaskPushNotificationConfig { Url = "http://localhost:5300/hook" } },
        });
        var span = await collector.WaitForAsync("a2a.push.deliver", TestHost.Timeout);
        await sender.StopAsync(CancellationToken.None);
        Assert.Equal(collector.Root.TraceId, span.TraceId);
        Assert.Equal(response.Task!.Id, span.GetTagItem(A2ATelemetryNames.TaskId));
        Assert.Equal("localhost", span.GetTagItem(A2ATelemetryNames.ServerAddress));
        Assert.Equal(1, span.GetTagItem(A2ATelemetryNames.PushAttempt));
        Assert.Equal("delivered", span.GetTagItem(A2ATelemetryNames.Outcome));
    }

    [Fact]
    public async Task Push_span_never_records_query_string_or_credentials()
    {
        var (spans, metrics, _) = await RunAsync(HttpStatusCode.OK, "http://localhost:5300/cb?sig=SECRET1", token: "SECRET3", credentials: "SECRET2");

        Assert.NotEmpty(spans);
        Assert.All(spans, s => Assert.Equal("localhost", s.GetTagItem(A2ATelemetryNames.ServerAddress)));
        var text = string.Join('|', spans.SelectMany(span => span.Tags.Select(t => $"{t.Key}={t.Value}")
            .Concat(span.Events.SelectMany(e => e.Tags.Select(t => $"{t.Key}={t.Value}"))).Append(span.DisplayName))
            .Concat(metrics.SelectMany(m => m.Tags.Select(t => $"{t.Key}={t.Value}"))));
        Assert.DoesNotContain("SECRET1", text);
        Assert.DoesNotContain("SECRET2", text);
        Assert.DoesNotContain("SECRET3", text);
    }

    [Fact]
    public async Task Push_metric_counts_retries_and_failure()
    {
        var (spans, metrics, _) = await RunAsync(HttpStatusCode.InternalServerError, "http://localhost:5300/hook", maxRetries: 2);

        // Per delivered event: two retries, one failure.
        Assert.NotEmpty(spans);
        Assert.Equal(2 * spans.Count, Count(metrics, "retried"));
        Assert.Equal(spans.Count, Count(metrics, "failed"));
        Assert.Equal(0, Count(metrics, "delivered"));
        Assert.All(spans, span =>
        {
            Assert.Equal("failed", span.GetTagItem(A2ATelemetryNames.Outcome));
            Assert.Equal(3, span.GetTagItem(A2ATelemetryNames.PushAttempt));
            Assert.Equal(ActivityStatusCode.Error, span.Status);
        });
    }

    [Fact]
    public async Task Rejected_4xx_counts_as_failed_without_retry()
    {
        var (spans, metrics, _) = await RunAsync(HttpStatusCode.BadRequest, "http://localhost:5300/hook");

        Assert.NotEmpty(spans);
        Assert.Equal(spans.Count, Count(metrics, "failed"));
        Assert.Equal(0, Count(metrics, "retried"));
        Assert.All(spans, span => Assert.Equal(1, span.GetTagItem(A2ATelemetryNames.PushAttempt)));
    }
}
