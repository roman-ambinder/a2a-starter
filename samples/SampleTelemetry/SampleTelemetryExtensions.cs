using System.Diagnostics;
using A2A.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SampleTelemetry;

/// <summary>
/// Opt-in OpenTelemetry for the sample hosts. Off unless <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set (and not empty) in the
/// builder's configuration; then nothing is registered at all. The libraries in <c>src/</c> never reference OpenTelemetry.
/// </summary>
public static class SampleTelemetryExtensions
{
    public const string EndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Activity source of the demo LLM client (<c>UseOpenTelemetry</c> in AgentServer).</summary>
    public const string LlmSource = "AgentServer.Llm";

    /// <summary>True when the endpoint key has a value. Environment variables are already a configuration source.</summary>
    public static bool IsEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return !string.IsNullOrWhiteSpace(configuration[EndpointKey]);
    }

    /// <summary>
    /// Adds traces, metrics and logs with an OTLP exporter (endpoint, protocol and headers come from the standard
    /// <c>OTEL_EXPORTER_OTLP_*</c> settings). Does nothing when <see cref="IsEnabled"/> is false. Call it after the host's
    /// <c>configure</c> callback, so the callback can switch telemetry off. The service name is <c>OTEL_SERVICE_NAME</c>, else
    /// <paramref name="serviceName"/>.
    /// </summary>
    public static WebApplicationBuilder AddSampleTelemetry(this WebApplicationBuilder builder, string serviceName, params string[] extraSources)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(extraSources);
        if (!IsEnabled(builder.Configuration)) return builder;

        var name = builder.Configuration["OTEL_SERVICE_NAME"] is { Length: > 0 } fromConfig ? fromConfig : serviceName;
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
        });
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(name))
            .WithTracing(t => t.AddSampleTracing(extraSources))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("System.Runtime")
                .AddMeter(A2ATelemetryNames.ServerMeter))
            .UseOtlpExporter();
        return builder;
    }

    /// <summary>
    /// The tracing part of <see cref="AddSampleTelemetry"/>, without an exporter: ASP.NET Core (no health checks), HttpClient,
    /// the A2A sources and <paramref name="extraSources"/>. Public so tests can check it with an in-memory processor.
    /// </summary>
    public static TracerProviderBuilder AddSampleTracing(this TracerProviderBuilder tracing, params string[] extraSources)
    {
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentNullException.ThrowIfNull(extraSources);
        return tracing
            .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
            .AddHttpClientInstrumentation(o =>
            {
                // The runtime sets url.full at start; scrub again at the end in case anything sets it later.
                o.EnrichWithHttpRequestMessage = (activity, _) => ScrubPushUrl(activity);
                o.EnrichWithHttpResponseMessage = (activity, _) => ScrubPushUrl(activity);
                o.EnrichWithException = (activity, _) => ScrubPushUrl(activity);
            })
            .AddSource(A2ATelemetryNames.ServerSource, A2ATelemetryNames.ClientSource)
            .AddSource(extraSources);
    }

    private const string PushDeliverSpan = "a2a.push.deliver";
    private static readonly string[] UrlTags = ["url.full", "http.url"];
    private static readonly string[] PathTags = ["url.path", "url.query", "http.target"];

    /// <summary>
    /// Cuts a webhook request's URL down to scheme://host[:port]. A webhook path can hold a secret (Slack-style
    /// <c>/services/.../&lt;secret&gt;</c>), so like the <c>a2a.push.deliver</c> span the HTTP span keeps the host only.
    /// A webhook request is one made under an <c>a2a.push.deliver</c> span. The parent chain is used because the
    /// HttpClient name ("a2a-push") never reaches the instrumentation. When telemetry is on, the push span always exists
    /// (its source is added above), and with the default parent-based sampler an HTTP span is only recorded when its push span is.
    /// </summary>
    private static void ScrubPushUrl(Activity activity)
    {
        if (!IsUnderPushDelivery(activity)) return;
        foreach (var tag in UrlTags)
            if (activity.GetTagItem(tag) is string value && Uri.TryCreate(value, UriKind.Absolute, out var url))
                activity.SetTag(tag, $"{url.Scheme}://{url.Authority}"); // Authority has no user info and no default port
        foreach (var tag in PathTags)
            if (activity.GetTagItem(tag) is not null) activity.SetTag(tag, null);
    }

    private static bool IsUnderPushDelivery(Activity activity)
    {
        for (var parent = activity.Parent; parent is not null; parent = parent.Parent)
            if (parent.OperationName == PushDeliverSpan && parent.Source.Name == A2ATelemetryNames.ServerSource) return true;
        return false;
    }
}
