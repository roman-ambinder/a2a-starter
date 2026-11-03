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
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddSource(A2ATelemetryNames.ServerSource, A2ATelemetryNames.ClientSource)
                .AddSource(extraSources))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("System.Runtime")
                .AddMeter(A2ATelemetryNames.ServerMeter))
            .UseOtlpExporter();
        return builder;
    }
}
