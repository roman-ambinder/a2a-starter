using Microsoft.Extensions.AI;
using SampleTelemetry;

namespace AgentServer.Llm;

/// <summary>Registers the demo <see cref="IChatClient"/> from <c>Llm:Provider</c>.</summary>
public static class ChatClientRegistration
{
    public const string ProviderKey = "Llm:Provider";
    public const string FakeProvider = "Fake";

    private static readonly string[] Phase2Providers = ["AzureOpenAI", "OpenAI", "Anthropic", "Ollama"];

    /// <summary>
    /// <c>Fake</c> (default) → singleton <see cref="FakeChatClient"/>, wrapped with OpenTelemetry only when telemetry is on. <c>AzureOpenAI</c>, <c>OpenAI</c>, <c>Anthropic</c>, <c>Ollama</c> →
    /// <see cref="NotSupportedException"/> "planned for Phase 2". Any other name → <see cref="NotSupportedException"/>.
    /// Names are case-insensitive.
    /// </summary>
    public static IServiceCollection AddDemoChatClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var provider = configuration[ProviderKey];
        if (string.IsNullOrWhiteSpace(provider) || provider.Equals(FakeProvider, StringComparison.OrdinalIgnoreCase))
        {
            if (SampleTelemetryExtensions.IsEnabled(configuration))
                // Spans for chat calls. EnableSensitiveData stays false: prompts and answers never reach a span.
                services.AddChatClient(sp => ActivatorUtilities.CreateInstance<FakeChatClient>(sp))
                    .UseOpenTelemetry(sourceName: SampleTelemetryExtensions.LlmSource);
            else
                services.AddSingleton<IChatClient, FakeChatClient>();
            return services;
        }

        if (Phase2Providers.FirstOrDefault(p => p.Equals(provider, StringComparison.OrdinalIgnoreCase)) is not null)
            throw new NotSupportedException($"LLM provider '{provider}' is planned for Phase 2. Use Llm:Provider=Fake.");
        throw new NotSupportedException(
            $"LLM provider '{provider}' is unknown. Use Llm:Provider=Fake (Phase 2 adds {string.Join(", ", Phase2Providers)}).");
    }
}
