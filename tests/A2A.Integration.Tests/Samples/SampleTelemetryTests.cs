using A2A.Integration.Tests.Fixtures;
using AgentServer;
using AgentServer.Llm;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SampleTelemetry;

namespace A2A.Integration.Tests.Samples;

public sealed class SampleTelemetryTests
{
    private static WebApplication Build(string endpoint) =>
        AgentServerApp.Build(["--environment", "Development"], b =>
        {
            b.Logging.ClearProviders();
            b.Configuration.AddInMemoryCollection(A2ATestHost.AgentServerConfig(A2ATestHost.PortBase + 3100, A2ATestHost.MockEntraBaseUrl));
            b.Configuration[SampleTelemetryExtensions.EndpointKey] = endpoint;
        });

    // Checks registration only. Resolving TracerProvider or MeterProvider would build the SDK: process-wide listeners and an
    // OTLP exporter, while other tests run in parallel. The app is never started, so its hosted services never build them either.
    private static bool IsRegistered<T>(WebApplication app) => app.Services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(T));

    [Fact]
    public async Task Telemetry_is_off_when_endpoint_unset()
    {
        await using var app = Build("");
        Assert.False(IsRegistered<TracerProvider>(app));
        Assert.False(IsRegistered<MeterProvider>(app));
        Assert.IsType<FakeChatClient>(app.Services.GetRequiredService<IChatClient>());
    }

    [Fact]
    public async Task Telemetry_is_on_when_endpoint_set()
    {
        await using var app = Build("http://127.0.0.1:4317");
        Assert.True(IsRegistered<TracerProvider>(app));
        Assert.True(IsRegistered<MeterProvider>(app));
        var chat = app.Services.GetRequiredService<IChatClient>();
        Assert.IsNotType<FakeChatClient>(chat);
        Assert.NotNull(chat.GetService<FakeChatClient>()); // wrapped, not replaced
    }

    [Fact]
    public async Task Message_content_capture_stays_off_even_if_the_environment_asks_for_it()
    {
        // The variable is set for this whole test process by CaptureContentEnvironment (the library reads it once).
        Assert.Equal("true", Environment.GetEnvironmentVariable(CaptureContentEnvironment.Variable));
        await using var app = Build("http://127.0.0.1:4317");
        var otel = app.Services.GetRequiredService<IChatClient>().GetService<OpenTelemetryChatClient>();
        Assert.NotNull(otel);
        Assert.False(otel.EnableSensitiveData);
    }
}

/// <summary>
/// Microsoft.Extensions.AI reads this variable once, as the default of <c>EnableSensitiveData</c>. Setting it before any test runs
/// makes the pin in <c>AddDemoChatClient</c> testable whatever the test order.
/// </summary>
internal static class CaptureContentEnvironment
{
    public const string Variable = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";

#pragma warning disable CA2255 // test process setup, not a library
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Set() => Environment.SetEnvironmentVariable(Variable, "true");
#pragma warning restore CA2255
}
