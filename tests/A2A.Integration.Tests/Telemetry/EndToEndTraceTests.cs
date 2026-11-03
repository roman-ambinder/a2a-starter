using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using A2A.Client;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;
using A2A.Tests.Shared;

namespace A2A.Integration.Tests.Telemetry;

[Collection(A2ATestHostCollection.Name)]
public sealed class EndToEndTraceTests(A2ATestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Random() => Guid.NewGuid().ToString("N");
    private static string WebhookUrl => A2ATestHost.ReceiverBaseUrl + "/webhook";

    private static SendMessageRequest Send(string text, TaskPushNotificationConfig? push = null) => new()
    {
        Message = new Message { MessageId = Random(), Role = Role.User, Parts = [Part.FromText(text)] },
        Configuration = push is null ? null : new SendMessageConfiguration { TaskPushNotificationConfig = push },
    };

    private static TaskPushNotificationConfig Config(string credentials, string token) => new()
    {
        Url = WebhookUrl,
        Token = token,
        Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = credentials },
    };

    private async Task SubscribeAsync(string token, string credentials)
    {
        // The task id is not known before the first send, so the subscription accepts any task ("*").
        var response = await host.Http.PostAsJsonAsync(A2ATestHost.ReceiverBaseUrl + "/api/subscriptions",
            new { taskId = "*", token, credentials }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task WaitForCompletedAsync(string taskId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            var elements = await host.Http.GetFromJsonAsync<List<JsonElement>>(
                $"{A2ATestHost.ReceiverBaseUrl}/api/notifications?taskId={Uri.EscapeDataString(taskId)}", Ct);
            var list = elements!.Select(e => e.Deserialize<StreamResponse>(A2AJson.Options)!).ToList();
            if (list.Count > 0 && list[^1].StatusUpdate?.Status.State == TaskState.Completed) return;
            await Task.Delay(25, timeout.Token);
        }
    }

    private static bool IsAncestor(ActivityCollector collector, Activity ancestor, Activity descendant)
    {
        var byId = collector.Stopped.ToDictionary(a => a.SpanId);
        for (var parent = descendant.ParentSpanId; byId.TryGetValue(parent, out var a); parent = a.ParentSpanId)
            if (a.SpanId == ancestor.SpanId) return true;
        return false;
    }

    [Fact]
    public async Task Push_reaches_webhook_receiver_in_same_trace()
    {
        using var collector = new ActivityCollector(A2ATelemetryNames.ClientSource, A2ATelemetryNames.ServerSource,
            "System.Net.Http", "Microsoft.AspNetCore");
        var (credentials, token) = ("cred-" + Random(), "tok-" + Random());
        await SubscribeAsync(token, credentials);
        await using var client = await host.CreateClientAsync("report", "JSONRPC", TestAuth.ApiKey, ct: Ct);

        var task = (await client.SendMessageAsync(Send("Traced push topic", Config(credentials, token)), Ct)).Task!;
        await WaitForCompletedAsync(task.Id);

        var deliver = collector.Stopped.First(a => a.OperationName == "a2a.push.deliver");
        Assert.Equal(collector.Root.TraceId, deliver.TraceId);
        // ASP.NET Core adds no url tags to its request activity here, so the /webhook request is found through the outgoing
        // HTTP client span of the delivery (url.full), which must sit under the deliver span.
        var outgoing = collector.Stopped.Where(a => a.Source.Name == "System.Net.Http"
                && a.GetTagItem("url.full") as string == WebhookUrl && IsAncestor(collector, deliver, a)).ToList();
        Assert.NotEmpty(outgoing);
        // The receiver's request activity continues the trace from the traceparent header of that outgoing request.
        var incoming = collector.Stopped.Where(a => a.OperationName == "Microsoft.AspNetCore.Hosting.HttpRequestIn"
                && outgoing.Any(o => o.SpanId == a.ParentSpanId)).ToList();
        Assert.NotEmpty(incoming);
        Assert.All(incoming, a => Assert.Equal(collector.Root.TraceId, a.TraceId));
        Assert.All(incoming, a => Assert.True(IsAncestor(collector, deliver, a)));
    }

    [Fact]
    public async Task No_secret_or_content_in_any_span()
    {
        // Every source, still limited to Root's trace by the collector's sampler.
        using var collector = new ActivityCollector(ActivityCollector.AllSources);
        var (credentials, pushToken, text) = ("cred-" + Random(), "tok-" + Random(), "content-" + Random());
        var bearer = await host.GetTokenAsync(TestAuth.Client, Ct);

        await using (var viaKey = await host.CreateClientAsync("report", "JSONRPC", TestAuth.ApiKey, ct: Ct))
            await viaKey.SendMessageAsync(Send(text), Ct);

        var card = await host.GetCardAsync("report", Ct);
        await using (var viaBearer = A2AClientFactory.Create(card, new A2AClientOptions
        {
            HttpClient = host.Http,
            Credentials = new RawHeaderCredentials("Authorization", "Bearer " + bearer),
            PreferredBinding = "JSONRPC",
        }))
            await viaBearer.SendMessageAsync(Send(text), Ct);

        await SubscribeAsync(pushToken, credentials);
        await using var pusher = await host.CreateClientAsync("report", "JSONRPC", TestAuth.ApiKey, ct: Ct);
        var task = (await pusher.SendMessageAsync(Send(text, Config(credentials, pushToken)), Ct)).Task!;
        await WaitForCompletedAsync(task.Id);

        var secrets = new[] { A2ATestHost.ApiKey, bearer, credentials, pushToken, text, "dev-client-secret" };
        var spans = collector.Stopped;
        Assert.Contains(spans, a => a.OperationName == "a2a.push.deliver");
        foreach (var span in spans)
        {
            var values = new List<string> { span.DisplayName, span.OperationName };
            values.AddRange(span.TagObjects.Select(t => t.Value?.ToString() ?? ""));
            values.AddRange(span.Events.SelectMany(e => e.Tags).Select(t => t.Value?.ToString() ?? ""));
            foreach (var secret in secrets)
                Assert.DoesNotContain(values, v => v.Contains(secret, StringComparison.Ordinal));
            // HttpClient already redacts a query to "?*"; that placeholder holds no value.
            if (span.GetTagItem("url.full") is string url) Assert.DoesNotContain('?', url.Replace("?*", ""));
        }
    }
}
