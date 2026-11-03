using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentServer.Llm;

/// <summary>
/// Deterministic stand-in for an LLM (Phase 1 has no real provider). The answer is built only from the last user message:
/// <c>"Fake LLM response to: {prompt}"</c>. Streaming yields the same text one word (with its trailing space) per update.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    public const string ModelId = "fake-llm";
    public const string Prefix = "Fake LLM response to: ";

    private static readonly ChatClientMetadata Metadata = new(nameof(FakeChatClient), defaultModelId: ModelId);

    /// <summary>The text the client answers for <paramref name="messages"/>.</summary>
    public static string Answer(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var prompt = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        return Prefix + prompt.Trim();
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer(messages))) { ModelId = ModelId });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var text = Answer(messages);
        var start = 0;
        while (start < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var space = text.IndexOf(' ', start);
            var end = space < 0 ? text.Length : space + 1;
            yield return new ChatResponseUpdate(ChatRole.Assistant, text[start..end]) { ModelId = ModelId };
            start = end;
            await Task.Yield();
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null) return null;
        if (serviceType == typeof(ChatClientMetadata)) return Metadata;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }
}
