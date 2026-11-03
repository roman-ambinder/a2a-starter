using AgentServer.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace A2A.Integration.Tests.Samples;

public sealed class FakeChatClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Response_is_deterministic_and_streaming_yields_the_same_words()
    {
        using var client = new FakeChatClient();
        ChatMessage[] prompt = [new(ChatRole.System, "be brief"), new(ChatRole.User, "Summarize Q3 sales")];

        var first = await client.GetResponseAsync(prompt, cancellationToken: Ct);
        var second = await client.GetResponseAsync(prompt, cancellationToken: Ct);
        Assert.Equal(first.Text, second.Text);
        Assert.Contains("Summarize Q3 sales", first.Text);

        var chunks = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync(prompt, cancellationToken: Ct)) chunks.Add(update.Text);
        Assert.True(chunks.Count > 1);
        Assert.Equal(first.Text, string.Concat(chunks));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Fake")]
    [InlineData("fake")]
    public void Fake_provider_registers_fake_chat_client(string? provider)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:Provider"] = provider }).Build();
        using var services = new ServiceCollection().AddDemoChatClient(config).BuildServiceProvider();
        Assert.IsType<FakeChatClient>(services.GetRequiredService<IChatClient>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://127.0.0.1:4317")]
    public void A_chat_client_registered_before_is_kept(string? otlpEndpoint)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = otlpEndpoint,
        }).Build();
        using var mine = new FakeChatClient();
        using var services = new ServiceCollection().AddSingleton<IChatClient>(mine).AddDemoChatClient(config).BuildServiceProvider();
        Assert.Same(mine, services.GetRequiredService<IChatClient>());
    }

    [Fact]
    public void Unknown_provider_name_is_rejected()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:Provider"] = "Nope" }).Build();
        Assert.Throws<NotSupportedException>(() => new ServiceCollection().AddDemoChatClient(config));
    }
}
