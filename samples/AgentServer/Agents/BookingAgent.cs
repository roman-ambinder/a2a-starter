using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using A2A.Protocol;
using A2A.Server.Agents;

namespace AgentServer.Agents;

/// <summary>
/// Books a flight in a short conversation. <c>book &lt;city&gt;</c> (Paris, London, Tokyo; any case) asks for a date; a valid
/// <c>yyyy-MM-dd</c> date on the same task completes with the <c>booking</c> artifact. The agent instance lives for one execution,
/// so the city of a running conversation is read back from the first user message in the stored task history.
/// </summary>
public sealed partial class BookingAgent : IAgentHandler
{
    public const string ArtifactId = "booking";
    private static readonly string[] Cities = ["Paris", "London", "Tokyo"];

    [GeneratedRegex(@"^\s*book\s+(?<city>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BookPattern();

    public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        if (context.CurrentTask is { } task)
        {
            var city = task.History?
                .Where(m => m.Role == Role.User)
                .Select(m => ParseCity(Text(m)))
                .FirstOrDefault(c => c is not null);
            if (city is not null)
            {
                await HandleDateAsync(context, sink, city, Text(context.Message).Trim(), ct);
                return;
            }
        }

        var match = BookPattern().Match(Text(context.Message));
        if (!match.Success)
        {
            await Say(context, sink, TaskState.InputRequired, "Where to? Try 'book Paris'.", ct);
            return;
        }

        var requested = match.Groups["city"].Value;
        var known = Cities.FirstOrDefault(c => c.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (known is null)
            await Say(context, sink, TaskState.Rejected, $"Sorry, we don't fly to {requested}.", ct);
        else
            await Say(context, sink, TaskState.InputRequired, "Which date? (YYYY-MM-DD)", ct);
    }

    private static async ValueTask HandleDateAsync(AgentRequestContext context, IAgentEventSink sink, string city, string input, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            await Say(context, sink, TaskState.InputRequired, "Please use YYYY-MM-DD.", ct);
            return;
        }

        var confirmation = "BK-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
        using var data = JsonSerializer.SerializeToDocument(new Dictionary<string, string>
        {
            ["city"] = city,
            ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["confirmation"] = confirmation,
        });
        await sink.AddArtifactAsync(new Artifact { ArtifactId = ArtifactId, Parts = [Part.FromData(data.RootElement.Clone())] },
            append: false, lastChunk: true, ct);
        await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
    }

    private static string? ParseCity(string text)
    {
        var match = BookPattern().Match(text);
        return match.Success ? Cities.FirstOrDefault(c => c.Equals(match.Groups["city"].Value, StringComparison.OrdinalIgnoreCase)) : null;
    }

    private static string Text(Message message) => message.Parts.FirstOrDefault(p => p.Text is not null)?.Text ?? "";

    private static ValueTask Say(AgentRequestContext context, IAgentEventSink sink, TaskState state, string text, CancellationToken ct)
        => sink.UpdateStatusAsync(state, new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = Role.Agent,
            TaskId = context.TaskId,
            ContextId = context.ContextId,
            Parts = [Part.FromText(text)],
        }, ct);
}
