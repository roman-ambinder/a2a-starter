using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ambinder.A2A.Client;
using Ambinder.A2A.Integration.Tests.Fixtures;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Push;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ambinder.A2A.Integration.Tests.Flows;

/// <summary>Push notifications end to end: the report agent pushes to the real WebhookReceiver (port base + 300).</summary>
[Collection(A2ATestHostCollection.Name)]
public sealed class PushFlowTests(A2ATestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Random() => Guid.NewGuid().ToString("N");

    private static SendMessageRequest Send(string text, TaskPushNotificationConfig? push = null) => new()
    {
        Message = new Message { MessageId = Random(), Role = Role.User, Parts = [Part.FromText(text)] },
        Configuration = push is null ? null : new SendMessageConfiguration { TaskPushNotificationConfig = push },
    };

    private static TaskPushNotificationConfig Config(string credentials, string token, string url) => new()
    {
        Url = url,
        Token = token,
        Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = credentials },
    };

    private static string WebhookUrl => A2ATestHost.ReceiverBaseUrl + "/webhook";

    private async Task<HttpResponseMessage> SubscribeAsync(string taskId, string token, string credentials)
        => await host.Http.PostAsJsonAsync(A2ATestHost.ReceiverBaseUrl + "/api/subscriptions", new { taskId, token, credentials }, Ct);

    private async Task<List<StreamResponse>> NotificationsAsync(string taskId)
    {
        var elements = await host.Http.GetFromJsonAsync<List<JsonElement>>(
            $"{A2ATestHost.ReceiverBaseUrl}/api/notifications?taskId={Uri.EscapeDataString(taskId)}", Ct);
        return elements!.Select(e => e.Deserialize<StreamResponse>(A2AJson.Options)!).ToList();
    }

    private static bool IsCompleted(StreamResponse e) => e.StatusUpdate?.Status.State == TaskState.Completed;

    private async Task<List<StreamResponse>> WaitForCompletedAsync(string taskId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            var list = await NotificationsAsync(taskId);
            if (list.Count > 0 && IsCompleted(list[^1])) return list;
            await Task.Delay(25, timeout.Token);
        }
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Report_push_delivers_all_events_to_receiver(string binding)
    {
        var (credentials, token) = (Random(), Random());
        // The task id is not known before the first send, so the subscription accepts any task ("*").
        Assert.Equal(HttpStatusCode.Created, (await SubscribeAsync("*", token, credentials)).StatusCode);
        await using var client = await host.CreateClientAsync("report", binding, TestAuth.ApiKey, ct: Ct);

        var task = (await client.SendMessageAsync(Send("Pushed topic " + binding, Config(credentials, token, WebhookUrl)), Ct)).Task!;

        var received = await WaitForCompletedAsync(task.Id);
        Assert.NotNull(received[0].Task);
        Assert.Contains(received, e => e.ArtifactUpdate?.Artifact.ArtifactId == "report");
        Assert.Contains(received, e => e.ArtifactUpdate?.Artifact.ArtifactId == "summary");
        Assert.Contains(received, e => e.ArtifactUpdate?.Artifact.ArtifactId == "data");
        Assert.Contains(received, e => e.StatusUpdate?.Status.State == TaskState.Working);
        Assert.Single(received, IsCompleted);
        Assert.Equal(1, received.Count(e => e.Task is not null));
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Push_config_crud_over_each_transport(string binding)
    {
        await using var client = await host.CreateClientAsync("report", binding, TestAuth.ApiKey, ct: Ct);
        var task = (await client.SendMessageAsync(Send("Config topic"), Ct)).Task!;

        var created = await client.CreateTaskPushNotificationConfigAsync(
            Config(Random(), Random(), WebhookUrl) with { TaskId = task.Id }, Ct);
        Assert.False(string.IsNullOrEmpty(created.Id));
        Assert.Equal(task.Id, created.TaskId);

        var got = await client.GetTaskPushNotificationConfigAsync(new GetTaskPushNotificationConfigRequest { TaskId = task.Id, Id = created.Id! }, Ct);
        Assert.Equal(created.Url, got.Url);
        Assert.Equal(created.Token, got.Token);

        var list = await client.ListTaskPushNotificationConfigsAsync(new ListTaskPushNotificationConfigsRequest { TaskId = task.Id }, Ct);
        Assert.Equal([created.Id], list.Configs.Select(c => c.Id));

        await client.DeleteTaskPushNotificationConfigAsync(new DeleteTaskPushNotificationConfigRequest { TaskId = task.Id, Id = created.Id! }, Ct);
        var after = await client.ListTaskPushNotificationConfigsAsync(new ListTaskPushNotificationConfigsRequest { TaskId = task.Id }, Ct);
        Assert.Empty(after.Configs);
        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            client.GetTaskPushNotificationConfigAsync(new GetTaskPushNotificationConfigRequest { TaskId = task.Id, Id = created.Id! }, Ct));
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.Code);
    }

    [Fact]
    public async Task Receiver_health_and_subscription_validation()
    {
        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync(A2ATestHost.ReceiverBaseUrl + "/health/live", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Http.GetAsync(A2ATestHost.ReceiverBaseUrl + "/health/ready", Ct)).StatusCode);
        var bad = await host.Http.PostAsJsonAsync(A2ATestHost.ReceiverBaseUrl + "/api/subscriptions", new { taskId = "t" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}

/// <summary>
/// The receiver's checks, with signatures made by the test itself: a second receiver (port base + 301) trusts a fake A2A server
/// (port base + 302) that serves the JWKS of a <see cref="PushSigningKey"/> the test holds, so any delivery can be forged or replayed.
/// </summary>
[Collection(A2ATestHostCollection.Name)]
public sealed class ReceiverVerificationTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly int ReceiverPort = A2ATestHost.PortBase + 301;
    private static readonly string ReceiverUrl = $"http://127.0.0.1:{ReceiverPort}";
    private static readonly string FakeServerUrl = $"http://127.0.0.1:{A2ATestHost.PortBase + 302}";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly HttpClient _http = new();
    private readonly PushSigningKey _key = new();
    // A second key in the fake JWKS, for tokens PushSigningKey cannot produce (non-standard lifetime).
    private readonly System.Security.Cryptography.RSA _rsa = System.Security.Cryptography.RSA.Create(2048);
    private readonly RsaSecurityKey _extraKey;
    private WebApplication? _fakeServer;
    private WebApplication? _receiver;

    public ReceiverVerificationTests() => _extraKey = new RsaSecurityKey(_rsa) { KeyId = "extra" };

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(FakeServerUrl);
        _fakeServer = builder.Build();
        _fakeServer.MapGet("/.well-known/a2a-push-jwks.json", () =>
        {
            var jwks = _key.GetJwks();
            var p = _rsa.ExportParameters(false);
            ((System.Text.Json.Nodes.JsonArray)jwks["keys"]!).Add(new System.Text.Json.Nodes.JsonObject
            {
                ["kty"] = "RSA", ["use"] = "sig", ["alg"] = "RS256", ["kid"] = "extra",
                ["n"] = Base64UrlEncoder.Encode(p.Modulus), ["e"] = Base64UrlEncoder.Encode(p.Exponent),
            });
            return Results.Json(jwks);
        });
        await _fakeServer.StartAsync();
        _receiver = await A2ATestHost.StartReceiverAsync(ReceiverPort, [FakeServerUrl]);
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        _key.Dispose();
        _rsa.Dispose();
        if (_receiver is not null) await _receiver.DisposeAsync();
        if (_fakeServer is not null) await _fakeServer.DisposeAsync();
    }

    private static string Random() => Guid.NewGuid().ToString("N");

    private static byte[] StatusBody(string taskId, string state = "TASK_STATE_WORKING")
        => JsonSerializer.SerializeToUtf8Bytes(new
        {
            statusUpdate = new { taskId, contextId = "c", status = new { state } },
        });

    private sealed record Delivery(string Credentials, string Token, byte[] Body, string Signature);

    private Delivery Valid(string taskId, string credentials, string token, byte[]? body = null)
    {
        body ??= StatusBody(taskId);
        return new Delivery(credentials, token, body, _key.CreateToken(FakeServerUrl, ReceiverUrl + "/webhook", taskId, body, Now));
    }

    private async Task<HttpStatusCode> PostAsync(Delivery d)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ReceiverUrl + "/webhook") { Content = new ByteArrayContent(d.Body) };
        request.Content.Headers.ContentType = new("application/a2a+json");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + d.Credentials);
        if (d.Token.Length > 0) request.Headers.TryAddWithoutValidation("X-A2A-Notification-Token", d.Token);
        if (d.Signature.Length > 0) request.Headers.TryAddWithoutValidation("X-A2A-Push-Signature", d.Signature);
        using var response = await _http.SendAsync(request, Ct);
        return response.StatusCode;
    }

    private async Task SubscribeAsync(string taskId, string token, string credentials)
    {
        var response = await _http.PostAsJsonAsync(ReceiverUrl + "/api/subscriptions", new { taskId, token, credentials }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<int> CountAsync(string taskId)
        => (await _http.GetFromJsonAsync<List<JsonElement>>($"{ReceiverUrl}/api/notifications?taskId={taskId}", Ct))!.Count;

    [Fact]
    public async Task Receiver_rejects_bad_credentials_token_signature_and_task_mismatch()
    {
        var (taskId, credentials, token) = (Random(), Random(), Random());
        await SubscribeAsync(taskId, token, credentials);
        var good = Valid(taskId, credentials, token);
        var otherTask = Random();
        var otherBody = StatusBody(otherTask);
        using var foreignKey = new PushSigningKey();

        var bad = new Dictionary<string, Delivery>
        {
            ["wrong credentials"] = good with { Credentials = Random() },
            ["wrong token"] = good with { Token = Random() },
            ["missing token"] = good with { Token = "" },
            ["missing signature"] = good with { Signature = "" },
            ["garbage signature"] = good with { Signature = "not.a.jwt" },
            ["signature of another key"] = good with { Signature = foreignKey.CreateToken(FakeServerUrl, ReceiverUrl + "/webhook", taskId, good.Body, Now) },
            ["untrusted issuer"] = good with { Signature = _key.CreateToken("http://evil.example", ReceiverUrl + "/webhook", taskId, good.Body, Now) },
            ["wrong audience"] = good with { Signature = _key.CreateToken(FakeServerUrl, "http://127.0.0.1:1/webhook", taskId, good.Body, Now) },
            ["expired"] = good with { Signature = _key.CreateToken(FakeServerUrl, ReceiverUrl + "/webhook", taskId, good.Body, Now - TimeSpan.FromHours(1)) },
            ["HS256 token"] = good with { Signature = HmacToken(taskId, good.Body, signed: true) },
            ["unsigned (alg none) token"] = good with { Signature = HmacToken(taskId, good.Body, signed: false) },
            ["iat in the future"] = good with { Signature = _key.CreateToken(FakeServerUrl, ReceiverUrl + "/webhook", taskId, good.Body, Now + TimeSpan.FromMinutes(2)) },
            ["body changed after signing"] = good with { Body = StatusBody(taskId, "TASK_STATE_COMPLETED") },
            ["signed task differs from payload"] = good with { Signature = _key.CreateToken(FakeServerUrl, ReceiverUrl + "/webhook", otherTask, good.Body, Now) },
            ["payload task differs from subscription"] = Valid(otherTask, credentials, token, otherBody),
        };
        foreach (var (name, delivery) in bad)
            Assert.True(HttpStatusCode.Unauthorized == await PostAsync(delivery), name);

        Assert.Equal(0, await CountAsync(taskId));
        Assert.Equal(0, await CountAsync(otherTask));

        Assert.Equal(HttpStatusCode.OK, await PostAsync(good)); // control: the same delivery, unchanged, is accepted
        Assert.Equal(1, await CountAsync(taskId));
    }

    private string HmacToken(string taskId, byte[] body, bool signed)
    {
        var claims = new Dictionary<string, object>
        {
            ["iss"] = FakeServerUrl, ["aud"] = ReceiverUrl + "/webhook", ["task_id"] = taskId,
            ["body_sha256"] = Base64UrlEncoder.Encode(System.Security.Cryptography.SHA256.HashData(body)),
            ["iat"] = Now.ToUnixTimeSeconds(), ["exp"] = Now.AddMinutes(5).ToUnixTimeSeconds(),
        };
        if (!signed)
        {
            string B64(object o) => Base64UrlEncoder.Encode(JsonSerializer.Serialize(o));
            return B64(new { alg = "none", typ = "JWT", kid = _key.Kid }) + "." + B64(claims) + ".";
        }
        var secret = new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) { KeyId = _key.Kid };
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims, SigningCredentials = new SigningCredentials(secret, SecurityAlgorithms.HmacSha256),
        });
    }

    [Fact]
    public async Task Receiver_rejects_token_with_too_long_lifetime()
    {
        var (taskId, credentials, token) = (Random(), Random(), Random());
        await SubscribeAsync(taskId, token, credentials);
        var body = StatusBody(taskId);
        var claims = new Dictionary<string, object>
        {
            ["task_id"] = taskId, ["body_sha256"] = Base64UrlEncoder.Encode(System.Security.Cryptography.SHA256.HashData(body)),
        };
        var jwt = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = FakeServerUrl, Audience = ReceiverUrl + "/webhook", IssuedAt = Now.UtcDateTime, Expires = Now.UtcDateTime.AddHours(1),
            Claims = claims, SigningCredentials = ForgedCredentials(),
        });
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(new Delivery(credentials, token, body, jwt)));
        Assert.Equal(0, await CountAsync(taskId));
    }

    private SigningCredentials ForgedCredentials() => new(_extraKey, SecurityAlgorithms.RsaSha256);

    [Fact]
    public async Task Wildcard_subscription_binds_to_first_task_and_rejects_others()
    {
        var (taskA, taskB, credentials, token) = (Random(), Random(), Random(), Random());
        await SubscribeAsync("*", token, credentials);

        Assert.Equal(HttpStatusCode.OK, await PostAsync(Valid(taskA, credentials, token)));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(Valid(taskB, credentials, token)));
        Assert.Equal(0, await CountAsync(taskB));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(Valid(taskA, credentials, token, StatusBody(taskA, "TASK_STATE_COMPLETED"))));
        Assert.Equal(2, await CountAsync(taskA));
    }

    [Fact]
    public async Task Receiver_deduplicates_redelivery()
    {
        var (taskId, credentials, token) = (Random(), Random(), Random());
        await SubscribeAsync(taskId, token, credentials);
        var body = StatusBody(taskId);

        Assert.Equal(HttpStatusCode.OK, await PostAsync(Valid(taskId, credentials, token, body)));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(Valid(taskId, credentials, token, body))); // new JWT, same body
        Assert.Equal(1, await CountAsync(taskId));

        Assert.Equal(HttpStatusCode.OK, await PostAsync(Valid(taskId, credentials, token, StatusBody(taskId, "TASK_STATE_COMPLETED"))));
        Assert.Equal(2, await CountAsync(taskId));
    }

    [Fact]
    public async Task Receiver_rejects_body_over_one_megabyte()
    {
        var (taskId, credentials, token) = (Random(), Random(), Random());
        await SubscribeAsync(taskId, token, credentials);
        var huge = new Delivery(credentials, token, new byte[1024 * 1024 + 1], "x");

        // Kestrel may answer 413 and close the connection while the client is still writing; the client then sees a reset instead.
        try { Assert.Equal(HttpStatusCode.RequestEntityTooLarge, await PostAsync(huge)); }
        catch (HttpRequestException) { }
        Assert.Equal(0, await CountAsync(taskId));
    }
}
