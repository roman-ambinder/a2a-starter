using A2A.Protocol;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AgentClient;

/// <summary>
/// Opt-in OpenTelemetry for the console client. Off unless <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set (and not empty).
/// It uses the SDK directly, not SampleTelemetry, so the client image does not need the ASP.NET runtime.
/// </summary>
public static class ClientTelemetry
{
    /// <summary>Activity source of the per-flow root spans.</summary>
    public const string SourceName = "AgentClient";

    private const string EndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// Starts the tracer and meter providers; null when the endpoint is unset. Dispose the result to flush before the process exits.
    /// The service name is <c>OTEL_SERVICE_NAME</c>, else <c>agentclient</c>.
    /// </summary>
    public static IDisposable? Start(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(configuration[EndpointKey])) return null;

        var name = configuration["OTEL_SERVICE_NAME"] is { Length: > 0 } fromConfig ? fromConfig : "agentclient";
        var resource = ResourceBuilder.CreateDefault().AddService(name);
        var tracer = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(SourceName, A2ATelemetryNames.ClientSource, "Grpc.Net.Client")
            .AddHttpClientInstrumentation()
            .AddOtlpExporter()
            .Build();
        var meter = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddHttpClientInstrumentation()
            .AddOtlpExporter()
            .Build();
        return new Holder(tracer, meter);
    }

    private sealed class Holder(TracerProvider tracer, MeterProvider meter) : IDisposable
    {
        // Dispose flushes pending data.
        public void Dispose()
        {
            tracer.Dispose();
            meter.Dispose();
        }
    }
}
