using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace WebhookReceiver;

/// <summary>Configuration section <c>Receiver</c>.</summary>
public sealed class ReceiverOptions
{
    public const string SectionName = "Receiver";
    public const string DefaultUrl = "http://localhost:5300";

    /// <summary>Base URLs of the A2A servers whose push signatures are accepted (<c>iss</c>). Default <c>http://localhost:5200</c>.</summary>
    public List<string> TrustedServers { get; set; } = [];

    /// <summary>This receiver's public base URL. The signature <c>aud</c> must equal <c>{PublicUrl}/webhook</c>.</summary>
    public string PublicUrl { get; set; } = DefaultUrl;

    public string WebhookUrl => PublicUrl.TrimEnd('/') + "/webhook";
}

/// <summary>
/// A webhook receiver that verifies A2A push notifications. A client registers what it expects (<c>POST /api/subscriptions</c>);
/// <c>POST /webhook</c> then accepts a delivery only when the Bearer credentials, the notification token, the signature JWT
/// (JWKS of a trusted server, audience, lifetime, body hash) and the task id all match. Anything else is 401 and nothing is stored.
/// </summary>
public static class WebhookReceiverApp
{
    public const long MaxBodyBytes = 1024 * 1024;

    /// <summary>Content root (appsettings*.json) below the output folder; see WebhookReceiver.csproj.</summary>
    public static string ContentRoot => Path.Combine(AppContext.BaseDirectory, "webhookreceiver-config");

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = ContentRoot });
        configure?.Invoke(builder);

        var options = builder.Configuration.GetSection(ReceiverOptions.SectionName).Get<ReceiverOptions>() ?? new ReceiverOptions();
        options.PublicUrl = PushSignatureValidator.Normalize(options.PublicUrl);
        options.TrustedServers = [.. (options.TrustedServers.Count == 0 ? ["http://localhost:5200"] : options.TrustedServers)
            .Select(PushSignatureValidator.Normalize)];
        if (string.IsNullOrEmpty(builder.Configuration["urls"]))
            builder.WebHost.UseUrls(ReceiverOptions.DefaultUrl);
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = MaxBodyBytes);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<NotificationStore>();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<PushSignatureValidator>();

        var app = builder.Build();

        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

        app.MapPost("/api/subscriptions", (SubscriptionRequest? request, NotificationStore store) =>
        {
            if (request is not { TaskId.Length: > 0, Token.Length: > 0, Credentials.Length: > 0 })
                return Results.BadRequest(new { error = "taskId, token and credentials are required" });
            store.AddSubscription(new Subscription(request.TaskId, request.Token, request.Credentials));
            return Results.Created($"/api/notifications?taskId={Uri.EscapeDataString(request.TaskId)}", new { taskId = request.TaskId });
        });

        app.MapGet("/api/notifications", (string? taskId, NotificationStore store) =>
            string.IsNullOrEmpty(taskId)
                ? Results.BadRequest(new { error = "taskId is required" })
                : Results.Json(store.Get(taskId)));

        app.MapPost("/webhook", async (HttpContext http, NotificationStore store, PushSignatureValidator validator, ILogger<WebhookReceiverLog> log) =>
        {
            var ct = http.RequestAborted;
            byte[] body;
            try { body = await ReadBodyAsync(http.Request, ct); }
            catch (BodyTooLargeException) { return Results.StatusCode(StatusCodes.Status413PayloadTooLarge); }

            IResult Reject(string reason)
            {
                log.LogWarning("Rejected a webhook delivery: {Reason}", reason); // never the credentials, token or signature
                return Results.Unauthorized();
            }

            // 1. Bearer credentials and 2. notification token must belong to one subscription.
            var authorization = http.Request.Headers.Authorization.ToString();
            var credentials = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..].Trim() : null;
            if (credentials is null) return Reject("missing or non-Bearer credentials");
            var matches = store.Match(credentials, http.Request.Headers["X-A2A-Notification-Token"].ToString());
            if (matches.Count == 0) return Reject("credentials or notification token do not match a subscription");

            // 3. Signature: trusted issuer, JWKS key, audience, lifetime, body hash.
            var signature = await validator.ValidateAsync(http.Request.Headers["X-A2A-Push-Signature"].ToString(), body, ct);
            if (!signature.Valid) return Reject(signature.Failure ?? "invalid signature");

            // 4. The task id in the payload, in the signed claim and in the subscription must be the same.
            JsonDocument doc;
            try { doc = JsonDocument.Parse(body); }
            catch (JsonException) { return Results.BadRequest(new { error = "body is not JSON" }); }
            using (doc)
            {
                var taskId = TaskIdOf(doc.RootElement);
                if (taskId is null || taskId != signature.TaskId) return Reject("task id does not match");

                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body));
                return store.TryAccept(matches, taskId, hash, doc.RootElement) switch
                {
                    NotificationStore.AcceptResult.Rejected => Reject("task id does not match the subscription"),
                    NotificationStore.AcceptResult.Stored => Results.Ok(new { status = "stored" }),
                    _ => Results.Ok(new { status = "duplicate" }),
                };
            }
        });

        return app;
    }

    private sealed class BodyTooLargeException : Exception;

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength > MaxBodyBytes) throw new BodyTooLargeException();
        request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize = MaxBodyBytes;
        using var ms = new MemoryStream();
        try { await request.Body.CopyToAsync(ms, ct); }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge) { throw new BodyTooLargeException(); }
        return ms.ToArray();
    }

    /// <summary>The task id of a StreamResponse: <c>task.id</c>, or <c>taskId</c> of statusUpdate, artifactUpdate or message.</summary>
    private static string? TaskIdOf(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("task", out var task) && task.ValueKind == JsonValueKind.Object)
            return Str(task, "id");
        foreach (var name in new[] { "statusUpdate", "artifactUpdate", "message" })
            if (root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Object)
                return Str(e, "taskId");
        return null;
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private sealed record SubscriptionRequest(string? TaskId, string? Token, string? Credentials);
}

/// <summary>Logger category for the webhook endpoint.</summary>
public sealed class WebhookReceiverLog;
