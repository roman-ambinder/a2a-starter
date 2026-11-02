namespace AgentServer.Agents;

/// <summary>The <c>FakeAgents</c> configuration section.</summary>
public sealed class FakeAgentOptions
{
    public const string SectionName = "FakeAgents";

    /// <summary>Pause between the demo agents' steps, so streaming and cancellation are visible.</summary>
    public TimeSpan StepDelay { get; set; } = TimeSpan.FromMilliseconds(500);
}
