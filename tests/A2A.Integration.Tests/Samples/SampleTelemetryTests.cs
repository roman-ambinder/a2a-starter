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

    [Fact]
    public async Task Telemetry_is_off_when_endpoint_unset()
    {
        await using var app = Build("");
        Assert.Null(app.Services.GetService<TracerProvider>());
        Assert.Null(app.Services.GetService<MeterProvider>());
        Assert.IsType<FakeChatClient>(app.Services.GetRequiredService<IChatClient>());
    }

    [Fact]
    public async Task Telemetry_is_on_when_endpoint_set()
    {
        await using var app = Build("http://127.0.0.1:4317");
        Assert.NotNull(app.Services.GetService<TracerProvider>());
        Assert.NotNull(app.Services.GetService<MeterProvider>());
        var chat = app.Services.GetRequiredService<IChatClient>();
        Assert.IsNotType<FakeChatClient>(chat);
        Assert.NotNull(chat.GetService<FakeChatClient>()); // wrapped, not replaced
    }
}
