using Ambinder.A2A.Server;
using System.Text.Json;
using Ambinder.A2A.Protocol;
using AgentServer.Cards;

namespace AgentServer.Agents;

/// <summary>
/// Replies with a direct message that mirrors the input parts: text becomes <c>"echo: " + text</c>; file and data parts are
/// copied as they are. Never creates a task. With the timestamp extension active, the reply carries an ISO UTC timestamp.
/// </summary>
public sealed class EchoAgent : IAgentHandler
{
    public const string TextPrefix = "echo: ";

    public ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        var parts = context.Message.Parts
            .Select(p => p.Text is { } text ? p with { Text = TextPrefix + text } : p)
            .ToList();

        var reply = new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = Role.Agent,
            ContextId = context.ContextId,
            Parts = parts,
        };

        if (context.ActiveExtensions.Contains(AgentCards.TimestampExtension))
        {
            reply = reply with
            {
                Extensions = [AgentCards.TimestampExtension],
                Metadata = JsonSerializer.SerializeToElement(new Dictionary<string, object>
                {
                    [AgentCards.TimestampExtension] = new Dictionary<string, string>
                    {
                        ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                    },
                }),
            };
        }

        return sink.ReplyAsync(reply, ct);
    }
}
