using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Push;
using Ambinder.A2A.Server.Tasks;
using Ambinder.A2A.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ambinder.A2A.Server.Tests.Push;

public class PushTests
{
    private sealed record Captured(Uri Url, HttpRequestHeaders Headers, string? ContentType, byte[] Body);

    /// <summary>Stands in for the network: records every request and answers with the supplied responder.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Captured> _requests = new();
        public Func<int, Task<HttpStatusCode>> Responder { get; set; } = _ => Task.FromResult(HttpStatusCode.OK);
        public IReadOnlyList<Captured> Requests => [.. _requests];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            _requests.Enqueue(new Captured(request.RequestUri!, request.Headers, request.Content?.Headers.ContentType?.ToString(), body));
            var status = await Responder(_requests.Count);
            return new HttpResponseMessage(status);
        }

        public async Task WaitForAsync(int count)
        {
            var deadline = DateTime.UtcNow + TestHost.Timeout;
            while (_requests.Count < count)
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException($"Expected {count} webhook requests, saw {_requests.Count}.");
                await Task.Delay(5);
            }
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public StubHandler Handler { get; } = new();
        public ServiceProvider Provider { get; }
        public PushNotificationSender Sender { get; }
        public IPushConfigStore Configs { get; }
        public TestHost Host { get; }

        public Harness(Action<PushOptions>? configure = null, bool pushCapable = true)
        {
            var services = new ServiceCollection().AddLogging();
            services.AddA2APush(o =>
            {
                o.InitialBackoff = TimeSpan.FromMilliseconds(10);
                o.AllowPrivateNetworks = true;
                o.Issuer = "https://agents.example.test";
                configure?.Invoke(o);
            });
            services.AddHttpClient("a2a-push").ConfigurePrimaryHttpMessageHandler(() => Handler);
            Provider = services.BuildServiceProvider();
            Sender = Provider.GetRequiredService<PushNotificationSender>();
            Configs = Provider.GetRequiredService<IPushConfigStore>();
            var card = TestHost.DefaultCard with { Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = pushCapable ? true : null } };
            var options = Provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PushOptions>>().Value;
            Host = new TestHost(card, observers: [Sender], pushConfigs: Configs, pushOptions: options);
            Sender.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            await Sender.StopAsync(CancellationToken.None);
            await Provider.DisposeAsync();
        }

        public Task<SendMessageResponse> SendWithPushAsync(TaskPushNotificationConfig config, bool returnImmediately = false)
            => Host.SendAsync(new SendMessageRequest
            {
                Message = TestHost.UserText("go"),
                Configuration = new SendMessageConfiguration { TaskPushNotificationConfig = config, ReturnImmediately = returnImmediately },
            });
    }

    private static TaskPushNotificationConfig Config(string url = "http://localhost:5300/hook", string? id = null) => new()
    {
        Id = id,
        Url = url,
        Token = "tok-123",
        Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = "secret-cred" },
    };

    private static async Task ThrowsA2A(A2AErrorCode code, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<A2AException>(action);
        Assert.Equal(code, ex.Code);
    }

    private static async Task<string> NewWorkingTaskAsync(Harness h)
    {
        var gate = new Gate();
        h.Host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            await gate.WaitAsync(ct);
        };
        var response = await h.Host.SendAsync(TestHost.Text("go", returnImmediately: true));
        return response.Task!.Id;
    }

    [Fact]
    public async Task Create_get_list_delete_push_config_round_trip()
    {
        await using var h = new Harness();
        var taskId = await NewWorkingTaskAsync(h);
        var call = TestHost.Call();

        var created = await h.Host.Manager.CreateTaskPushNotificationConfigAsync(call, Config() with { TaskId = taskId }, CancellationToken.None);
        Assert.True(Guid.TryParse(created.Id, out _));
        Assert.Equal(taskId, created.TaskId);
        Assert.Equal("tok-123", created.Token);

        var second = await h.Host.Manager.CreateTaskPushNotificationConfigAsync(call, Config(id: "mine") with { TaskId = taskId }, CancellationToken.None);
        Assert.Equal("mine", second.Id);

        var got = await h.Host.Manager.GetTaskPushNotificationConfigAsync(call, new GetTaskPushNotificationConfigRequest { TaskId = taskId, Id = created.Id! }, CancellationToken.None);
        Assert.Equal(created, got);

        var list = await h.Host.Manager.ListTaskPushNotificationConfigsAsync(call, new ListTaskPushNotificationConfigsRequest { TaskId = taskId }, CancellationToken.None);
        Assert.Equal([created.Id, "mine"], list.Configs.Select(c => c.Id));
        Assert.Null(list.NextPageToken);

        var page1 = await h.Host.Manager.ListTaskPushNotificationConfigsAsync(call, new ListTaskPushNotificationConfigsRequest { TaskId = taskId, PageSize = 1 }, CancellationToken.None);
        Assert.Equal(created.Id, Assert.Single(page1.Configs).Id);
        Assert.False(string.IsNullOrEmpty(page1.NextPageToken));
        var page2 = await h.Host.Manager.ListTaskPushNotificationConfigsAsync(call, new ListTaskPushNotificationConfigsRequest { TaskId = taskId, PageSize = 1, PageToken = page1.NextPageToken }, CancellationToken.None);
        Assert.Equal("mine", Assert.Single(page2.Configs).Id);
        Assert.True(string.IsNullOrEmpty(page2.NextPageToken));

        await h.Host.Manager.DeleteTaskPushNotificationConfigAsync(call, new DeleteTaskPushNotificationConfigRequest { TaskId = taskId, Id = created.Id! }, CancellationToken.None);
        await ThrowsA2A(A2AErrorCode.TaskNotFound, () => h.Host.Manager.GetTaskPushNotificationConfigAsync(call, new GetTaskPushNotificationConfigRequest { TaskId = taskId, Id = created.Id! }, CancellationToken.None));
        var after = await h.Host.Manager.ListTaskPushNotificationConfigsAsync(call, new ListTaskPushNotificationConfigsRequest { TaskId = taskId }, CancellationToken.None);
        Assert.Equal("mine", Assert.Single(after.Configs).Id);

        // Another caller cannot see the task or its configs.
        await ThrowsA2A(A2AErrorCode.TaskNotFound, () => h.Host.Manager.ListTaskPushNotificationConfigsAsync(TestHost.Call("mallory"), new ListTaskPushNotificationConfigsRequest { TaskId = taskId }, CancellationToken.None));
        await ThrowsA2A(A2AErrorCode.TaskNotFound, () => h.Host.Manager.CreateTaskPushNotificationConfigAsync(call, Config() with { TaskId = "nope" }, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_is_idempotent()
    {
        await using var h = new Harness();
        var taskId = await NewWorkingTaskAsync(h);
        var call = TestHost.Call();
        var request = new DeleteTaskPushNotificationConfigRequest { TaskId = taskId, Id = "never-existed" };

        await h.Host.Manager.DeleteTaskPushNotificationConfigAsync(call, request, CancellationToken.None);
        await h.Host.Manager.DeleteTaskPushNotificationConfigAsync(call, request, CancellationToken.None);
        // The task itself must still exist.
        await ThrowsA2A(A2AErrorCode.TaskNotFound, () => h.Host.Manager.DeleteTaskPushNotificationConfigAsync(call, request with { TaskId = "nope" }, CancellationToken.None));
    }

    [Fact]
    public async Task Push_on_agent_without_capability_throws_not_supported()
    {
        await using var h = new Harness(pushCapable: false);
        var m = h.Host.Manager;
        var call = TestHost.Call();
        const string code = "any-task"; // the capability check comes before the task lookup

        await ThrowsA2A(A2AErrorCode.PushNotificationNotSupported, () => m.CreateTaskPushNotificationConfigAsync(call, Config() with { TaskId = code }, CancellationToken.None));
        await ThrowsA2A(A2AErrorCode.PushNotificationNotSupported, () => m.GetTaskPushNotificationConfigAsync(call, new GetTaskPushNotificationConfigRequest { TaskId = code, Id = "x" }, CancellationToken.None));
        await ThrowsA2A(A2AErrorCode.PushNotificationNotSupported, () => m.ListTaskPushNotificationConfigsAsync(call, new ListTaskPushNotificationConfigsRequest { TaskId = code }, CancellationToken.None));
        await ThrowsA2A(A2AErrorCode.PushNotificationNotSupported, () => m.DeleteTaskPushNotificationConfigAsync(call, new DeleteTaskPushNotificationConfigRequest { TaskId = code, Id = "x" }, CancellationToken.None));
        await ThrowsA2A(A2AErrorCode.PushNotificationNotSupported, () => h.SendWithPushAsync(Config()));
    }

    [Fact]
    public async Task Every_task_event_is_posted_as_stream_response()
    {
        await using var h = new Harness();
        h.Host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            await sink.AddArtifactAsync(new Artifact { ArtifactId = "a1", Parts = [Part.FromText("data")] }, false, true, ct);
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
        };

        var task = (await h.SendWithPushAsync(Config("http://localhost:5300/hook"))).Task!;
        await h.Handler.WaitForAsync(4);

        var requests = h.Handler.Requests;
        Assert.Equal(4, requests.Count);
        var events = requests.Select(r => JsonSerializer.Deserialize<StreamResponse>(r.Body, A2AJson.Options)!).ToList();
        Assert.NotNull(events[0].Task);
        Assert.Equal(task.Id, events[0].Task!.Id);
        Assert.Equal(TaskState.Working, events[1].StatusUpdate!.Status.State);
        Assert.Equal("a1", events[2].ArtifactUpdate!.Artifact.ArtifactId);
        Assert.Equal(TaskState.Completed, events[3].StatusUpdate!.Status.State);

        foreach (var r in requests)
        {
            Assert.Equal("http://localhost:5300/hook", r.Url.ToString());
            Assert.Equal("application/a2a+json", r.ContentType);
            Assert.Equal("Bearer secret-cred", Assert.Single(r.Headers.GetValues("Authorization")));
            Assert.Equal("tok-123", Assert.Single(r.Headers.GetValues("X-A2A-Notification-Token")));
            Assert.False(string.IsNullOrEmpty(Assert.Single(r.Headers.GetValues("X-A2A-Push-Signature"))));
        }

        // The task is final, so its configs are gone once the last delivery finished.
        await Eventually(async () =>
            (await h.Host.Manager.ListTaskPushNotificationConfigsAsync(TestHost.Call(), new ListTaskPushNotificationConfigsRequest { TaskId = task.Id }, CancellationToken.None)).Configs.Count == 0);
    }

    [Fact]
    public async Task Push_signature_verifies_and_binds_body_hash()
    {
        await using var h = new Harness();
        var webhook = "https://hooks.example.test/a2a";
        var task = (await h.SendWithPushAsync(Config(webhook))).Task!;
        await h.Handler.WaitForAsync(2);
        var request = h.Handler.Requests[0];
        var jwt = Assert.Single(request.Headers.GetValues("X-A2A-Push-Signature"));

        var jwks = new JsonWebKeySet(h.Provider.GetRequiredService<PushSigningKey>().GetJwks().ToJsonString());
        var key = Assert.Single(jwks.Keys);
        Assert.Equal("RSA", key.Kty);
        Assert.Equal("sig", key.Use);
        Assert.Equal("RS256", key.Alg);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(jwt, new TokenValidationParameters
        {
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidIssuer = "https://agents.example.test",
            ValidAudience = webhook,
            ValidAlgorithms = ["RS256"],
        });
        Assert.True(result.IsValid, result.Exception?.Message);

        var token = (JsonWebToken)result.SecurityToken;
        Assert.Equal(key.Kid, token.Kid);
        Assert.Equal(task.Id, token.GetPayloadValue<string>("task_id"));
        Assert.Equal(Base64UrlEncoder.Encode(SHA256.HashData(request.Body)), token.GetPayloadValue<string>("body_sha256"));
        Assert.Equal(TimeSpan.FromMinutes(5), token.ValidTo - token.IssuedAt);

        // A tampered body no longer matches the signed hash.
        var tampered = request.Body.Concat("x"u8.ToArray()).ToArray();
        Assert.NotEqual(Base64UrlEncoder.Encode(SHA256.HashData(tampered)), token.GetPayloadValue<string>("body_sha256"));
    }

    [Fact]
    public async Task Failing_webhook_does_not_block_task_and_is_retried()
    {
        await using var h = new Harness();
        using var deliveries = new ActivityCollector(A2ATelemetryNames.ServerSource);
        var gate = new Gate();
        h.Handler.Responder = async _ =>
        {
            await gate.WaitAsync(CancellationToken.None);
            return HttpStatusCode.InternalServerError;
        };

        // The webhook hangs and then fails; the blocking send must still return.
        var response = await h.SendWithPushAsync(Config());
        Assert.Equal(TaskState.Completed, response.Task!.Status.State);

        gate.Open();
        // Two events (task snapshot, completed), each tried once plus 3 retries.
        await h.Handler.WaitForAsync(8);
        // A delivery span stops when its retry loop ends, so no attempt can follow once both have stopped.
        await deliveries.WaitForCountAsync("a2a.push.deliver", 2, TestHost.Timeout);
        var bodies = h.Handler.Requests.GroupBy(r => Convert.ToHexString(r.Body)).ToList();
        Assert.Equal(2, bodies.Count);
        Assert.All(bodies, g => Assert.Equal(4, g.Count()));
    }

    [Fact]
    public async Task Retry_stops_after_first_success()
    {
        await using var h = new Harness();
        using var deliveries = new ActivityCollector(A2ATelemetryNames.ServerSource);
        h.Handler.Responder = n => Task.FromResult(n < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NoContent);
        h.Host.Scripts.Execute = (_, sink, ct) => sink.UpdateStatusAsync(TaskState.Working, null, ct);

        await h.SendWithPushAsync(Config(), returnImmediately: true);
        await h.Handler.WaitForAsync(3);
        // Events of one task are delivered in order, so the first span to stop is event 1's, with its retries done.
        await deliveries.WaitForCountAsync("a2a.push.deliver", 1, TestHost.Timeout);
        // Event 1 needed 3 attempts; later events (working) follow with their own first attempt succeeding.
        Assert.Equal(3, h.Handler.Requests.Count(r => r.Body.SequenceEqual(h.Handler.Requests[0].Body)));
    }

    [Theory]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://10.0.0.5/hook")]
    [InlineData("http://[fd00::1]/hook")]
    [InlineData("http://localhost/hook")]
    [InlineData("https://192.168.1.10/hook")]
    [InlineData("https://169.254.169.254/latest")]
    [InlineData("https://[::1]/hook")]
    [InlineData("ftp://example.com/x")]
    [InlineData("http://93.184.216.34/hook")] // public but not HTTPS
    [InlineData("https://[::ffff:127.0.0.1]/hook")]
    [InlineData("https://[64:ff9b::a00:5]/hook")]       // NAT64 -> 10.0.0.5
    [InlineData("https://[2002:a9fe:a9fe::1]/hook")]    // 6to4 -> 169.254.169.254
    [InlineData("https://[::127.0.0.1]/hook")]          // IPv4-compatible
    [InlineData("https://192.0.2.1/hook")]
    [InlineData("https://198.18.0.1/hook")]
    [InlineData("https://198.51.100.7/hook")]
    [InlineData("https://203.0.113.9/hook")]
    [InlineData("https://192.0.0.8/hook")]
    public async Task Private_network_url_rejected_when_not_allowed(string url)
    {
        await using var h = new Harness(o => o.AllowPrivateNetworks = false);
        var taskId = await NewWorkingTaskAsync(h);

        await ThrowsA2A(A2AErrorCode.InvalidParams, () => h.Host.Manager.CreateTaskPushNotificationConfigAsync(TestHost.Call(), Config(url) with { TaskId = taskId }, CancellationToken.None));
        await ThrowsA2A(A2AErrorCode.InvalidParams, () => h.SendWithPushAsync(Config(url)));
        Assert.Throws<A2AException>(() => WebhookUrlValidator.Validate(new Uri(url), allowPrivateNetworks: false));
    }

    [Fact]
    public async Task Public_https_url_is_accepted_and_non_http_scheme_is_always_rejected()
    {
        WebhookUrlValidator.Validate(new Uri("https://93.184.216.34/hook"), allowPrivateNetworks: false);
        WebhookUrlValidator.Validate(new Uri("http://127.0.0.1:5300/hook"), allowPrivateNetworks: true);
        var ex = Assert.Throws<A2AException>(() => WebhookUrlValidator.Validate(new Uri("ftp://example.com/x"), allowPrivateNetworks: true));
        Assert.Equal(A2AErrorCode.InvalidParams, ex.Code);

        await using var h = new Harness();
        var taskId = await NewWorkingTaskAsync(h);
        await ThrowsA2A(A2AErrorCode.InvalidParams, () => h.Host.Manager.CreateTaskPushNotificationConfigAsync(TestHost.Call(), Config("not a url") with { TaskId = taskId }, CancellationToken.None));
        await ThrowsA2A(A2AErrorCode.InvalidParams, () => h.Host.Manager.CreateTaskPushNotificationConfigAsync(TestHost.Call(), Config("") with { TaskId = taskId }, CancellationToken.None));
    }

    [Fact]
    public async Task Client_error_is_not_retried_but_429_is()
    {
        await using var h = new Harness();
        using (var deliveries = new ActivityCollector(A2ATelemetryNames.ServerSource))
        {
            h.Handler.Responder = _ => Task.FromResult(HttpStatusCode.BadRequest);
            await h.SendWithPushAsync(Config());
            await deliveries.WaitForCountAsync("a2a.push.deliver", 2, TestHost.Timeout); // two events, one attempt each
        }
        Assert.Equal(2, h.Handler.Requests.Count);

        await using var h2 = new Harness();
        using var retried = new ActivityCollector(A2ATelemetryNames.ServerSource);
        h2.Handler.Responder = _ => Task.FromResult((HttpStatusCode)429);
        await h2.SendWithPushAsync(Config());
        await retried.WaitForCountAsync("a2a.push.deliver", 2, TestHost.Timeout);
        Assert.Equal(8, h2.Handler.Requests.Count);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("169.254.169.254")]
    public async Task Connect_time_check_rejects_private_hosts(string host)
    {
        // Stands in for DNS rebinding: whatever the name resolved to at creation, the connect-time check refuses private results.
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await WebhookUrlValidator.ResolveCheckedAsync(host, allowPrivateNetworks: false, TestContext.Current.CancellationToken));
        var allowed = await WebhookUrlValidator.ResolveCheckedAsync(host, allowPrivateNetworks: true, TestContext.Current.CancellationToken);
        Assert.NotEmpty(allowed);

        var endpoint = new DnsEndPoint(host, 9);
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await WebhookUrlValidator.ConnectCheckedAsync(false, endpoint, TestContext.Current.CancellationToken));
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TestHost.Timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met in time.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public void Push_http_client_never_uses_a_proxy_or_follows_redirects()
    {
        using var provider = new ServiceCollection().AddLogging().AddA2APush().BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        HttpMessageHandler? handler = factory.CreateHandler(PushNotificationSender.HttpClientName);
        while (handler is DelegatingHandler d) handler = d.InnerHandler;

        var sockets = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(sockets.UseProxy);
        Assert.False(sockets.AllowAutoRedirect);
        Assert.NotNull(sockets.ConnectCallback);
    }
}
