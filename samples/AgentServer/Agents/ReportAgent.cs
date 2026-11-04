using System.Text;
using System.Text.Json;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentServer.Agents;

/// <summary>
/// Writes a short report on the topic (the input text). Steps, with <see cref="FakeAgentOptions.StepDelay"/> between them:
/// WORKING, <c>report.md</c> in three chunks (text from the <see cref="IChatClient"/>), <c>summary</c> (JSON), <c>data.csv</c>, COMPLETED.
/// Input containing "fail" (any case) ends FAILED. On cancellation it stops quietly; the task manager sets CANCELED.
/// </summary>
public sealed class ReportAgent(IChatClient chat, IOptions<FakeAgentOptions> options) : IAgentHandler
{
    public const string ReportArtifactId = "report";
    public const string SummaryArtifactId = "summary";
    public const string DataArtifactId = "data";
    public const int SectionCount = 3;

    private static readonly string[] Sections = ["Introduction", "Findings", "Conclusion"];

    public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        var topic = (context.Message.Parts.FirstOrDefault(p => p.Text is not null)?.Text ?? "").Trim();
        var delay = options.Value.StepDelay;
        try
        {
            await sink.UpdateStatusAsync(TaskState.Working, AgentMessage(context, "Collecting data"), ct);
            if (!await PauseAsync(delay, ct)) return;

            if (topic.Contains("fail", StringComparison.OrdinalIgnoreCase))
            {
                await sink.UpdateStatusAsync(TaskState.Failed,
                    AgentMessage(context, $"Report generation failed for topic '{topic}'"), ct);
                return;
            }

            for (var i = 0; i < SectionCount; i++)
            {
                var text = await WriteSectionAsync(topic, Sections[i], ct);
                var artifact = new Artifact
                {
                    ArtifactId = ReportArtifactId,
                    Name = "report.md",
                    Parts = [Part.FromText(i == 0 ? $"# Report: {topic}\n\n{text}\n\n" : text + "\n\n", "text/markdown")],
                };
                await sink.AddArtifactAsync(artifact, append: i > 0, lastChunk: i == SectionCount - 1, ct);
                if (!await PauseAsync(delay, ct)) return;
            }

            using var summary = JsonSerializer.SerializeToDocument(new Dictionary<string, object> { ["topic"] = topic, ["sections"] = SectionCount });
            await sink.AddArtifactAsync(new Artifact
            {
                ArtifactId = SummaryArtifactId,
                Parts = [Part.FromData(summary.RootElement.Clone())],
            }, append: false, lastChunk: true, ct);
            if (!await PauseAsync(delay, ct)) return;

            await sink.AddArtifactAsync(new Artifact
            {
                ArtifactId = DataArtifactId,
                Name = "data.csv",
                Parts = [Part.FromRaw(Encoding.UTF8.GetBytes(BuildCsv(topic)), "text/csv", "data.csv")],
            }, append: false, lastChunk: true, ct);
            if (!await PauseAsync(delay, ct)) return;

            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // PauseAsync absorbs cancellation during delays; this catch covers the sink and chat calls, which throw it.
            // Canceled: the task manager sets CANCELED.
        }
    }

    private async Task<string> WriteSectionAsync(string topic, string section, CancellationToken ct)
    {
        var response = await chat.GetResponseAsync(
            [new ChatMessage(ChatRole.User, $"Write the {section} section of a short report about: {topic}")], cancellationToken: ct);
        return response.Text;
    }

    /// <summary>Waits for the step delay; false when canceled.</summary>
    private static async ValueTask<bool> PauseAsync(TimeSpan delay, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static string BuildCsv(string topic)
    {
        var quoted = "\"" + topic.Replace("\"", "\"\"") + "\"";
        return $"topic,metric,value\n{quoted},alpha,{topic.Length}\n{quoted},beta,{topic.Length * 2}\n{quoted},gamma,{topic.Length * 3}\n";
    }

    private static Message AgentMessage(AgentRequestContext context, string text) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = Role.Agent,
        TaskId = context.TaskId,
        ContextId = context.ContextId,
        Parts = [Part.FromText(text)],
    };
}
