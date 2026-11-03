using System.Collections.Concurrent;
using System.Diagnostics;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.AspNetCore.Hosting;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Server.Push;
using Ambinder.A2A.Server.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;
using SampleTelemetry;

namespace Ambinder.A2A.Integration.Tests.Telemetry;

/// <summary>
/// Push webhook requests over a real socket, so the runtime's HttpClient diagnostics run (a stub handler would skip them).
/// Not parallel: one test starts the OpenTelemetry SDK, whose listeners are process-wide; the other needs no listener at all.
/// </summary>
[Collection(TelemetrySdkCollection.Name)]
public sealed class PushWireTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string PathSecret = "s3cr3t-path-9f2c";
    private const string QuerySecret = "s3cr3t-query-7d1a";

    [Fact]
    public async Task Push_request_span_from_sample_tracing_has_no_url_path()
    {
        var spans = new ConcurrentQueue<Activity>();
        await using var webhook = await LoopbackWebhook.StartAsync();
        using (var tracing = Sdk.CreateTracerProviderBuilder()
            .AddSampleTracing()
            .AddProcessor(new CollectingProcessor(spans))
            .Build())
        {
            await PushOnceAsync(webhook, $"{webhook.BaseUrl}/services/T000/B000/{PathSecret}?sig={QuerySecret}");
            await WaitForAsync(() => spans.Any(s => s.Source.Name == "System.Net.Http"));
        }

        var http = Assert.Single(spans, s => s.Source.Name == "System.Net.Http");
        Assert.Contains(spans, s => s.OperationName == "a2a.push.deliver" && s.SpanId == http.ParentSpanId);
        Assert.Equal(webhook.BaseUrl, http.GetTagItem("url.full"));
        // The webhook's own server span (ASP.NET Core in this process) sees its path; that is the receiver's business.
        foreach (var span in spans.Where(s => s.Kind != ActivityKind.Server))
        {
            var values = span.TagObjects.Select(t => t.Value?.ToString() ?? "").Append(span.DisplayName).Append(span.StatusDescription ?? "");
            Assert.DoesNotContain(values, v => v.Contains(PathSecret, StringComparison.Ordinal) || v.Contains(QuerySecret, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Push_request_has_no_traceparent_when_nothing_listens()
    {
        // Off means off: no listener, no current activity, so .NET adds no trace header to the webhook request.
        Assert.Null(Activity.Current);
        await using var webhook = await LoopbackWebhook.StartAsync();

        await PushOnceAsync(webhook, webhook.BaseUrl + "/webhook");

        var headers = Assert.Single(webhook.Requests);
        Assert.False(headers.ContainsKey("traceparent"), "The webhook request should carry no traceparent.");
        Assert.False(headers.ContainsKey("tracestate"));
    }

    /// <summary>Queues one working-status event for a task with one webhook config, and waits until the webhook got it.</summary>
    private static async Task PushOnceAsync(LoopbackWebhook webhook, string url)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddA2AServer(o =>
        {
            o.PublicBaseUrl = "http://127.0.0.1";
            o.Push.AllowPrivateNetworks = true;
            o.Push.MaxRetries = 0;
        });
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<PushNotificationSender>();
        var configs = provider.GetRequiredService<IPushConfigStore>();
        await sender.StartAsync(Ct);
        try
        {
            var taskId = Guid.NewGuid().ToString("N");
            await configs.SaveAsync("echo", taskId, new TaskPushNotificationConfig { TaskId = taskId, Url = url }, Ct);
            var status = new AgentTaskStatus { State = TaskState.Working };
            var task = new StoredTask("echo", "owner", null, new AgentTask { Id = taskId, ContextId = "ctx", Status = status });
            await sender.OnTaskEventAsync(task, new StreamResponse
            {
                StatusUpdate = new TaskStatusUpdateEvent { TaskId = taskId, ContextId = "ctx", Status = status },
            }, Ct);
            await WaitForAsync(() => !webhook.Requests.IsEmpty);
        }
        finally
        {
            await sender.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(10, Ct);
        }
    }

    private sealed class CollectingProcessor(ConcurrentQueue<Activity> spans) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => spans.Enqueue(data);
    }

    /// <summary>A webhook on a loopback port that answers 200 and keeps the request headers.</summary>
    private sealed class LoopbackWebhook : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private LoopbackWebhook(WebApplication app, string baseUrl)
        {
            _app = app;
            BaseUrl = baseUrl;
        }

        public string BaseUrl { get; }
        public ConcurrentQueue<IHeaderDictionary> Requests { get; } = new();

        public static async Task<LoopbackWebhook> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            LoopbackWebhook? self = null;
            app.MapPost("/{**path}", (HttpRequest request) =>
            {
                self!.Requests.Enqueue(new HeaderDictionary(request.Headers.ToDictionary(h => h.Key, h => h.Value)));
                return Results.Ok();
            });
            await app.StartAsync(Ct);
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            self = new LoopbackWebhook(app, address);
            return self;
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }
}

/// <summary>Tests that start the OpenTelemetry SDK, or that need no listener at all, run alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetrySdkCollection
{
    public const string Name = "Telemetry SDK";
}
