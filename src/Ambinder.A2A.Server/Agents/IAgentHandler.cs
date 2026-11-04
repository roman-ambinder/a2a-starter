using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server;

/// <summary>
/// An agent's logic. A new instance is created for each execution (and for each cancel).
/// </summary>
/// <remarks>
/// <para>
/// The server creates the handler with the dependency injection container, so it may take any registered service in its
/// constructor. Register the handler type with <see cref="Hosting.A2AServerBuilder.AddAgent{THandler}"/>; do not register it yourself.
/// </para>
/// <para>
/// Because a new instance is created per execution and per cancel, keep no state in fields between calls. Keep state in the task
/// (artifacts and history) or in your own store.
/// </para>
/// <para>
/// An <c>HttpContext</c> is not available. A handler runs on a background task, after the HTTP request may have ended.
/// Use <see cref="AgentRequestContext.User"/> for the caller. Many executions run at the same time.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class EchoAgent : IAgentHandler
/// {
///     public async ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
///     {
///         string text = context.Message.Parts.FirstOrDefault()?.Text ?? "";
///         await sink.ReplyAsync(
///             new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.Agent, Parts = [Part.FromText("You said: " + text)] },
///             ct);
///     }
/// }
/// </code>
/// </example>
public interface IAgentHandler
{
    /// <summary>
    /// Handles one user message. Report progress through <paramref name="sink"/>: either one
    /// <see cref="IAgentEventSink.ReplyAsync"/> (direct message, no task), or status and artifact
    /// updates ending in a terminal or interrupted state. <paramref name="ct"/> is cancelled when the
    /// task is cancelled; it is not the caller's request token.
    /// With <c>returnImmediately</c>, SendMessage returns at the agent's first event, so emit WORKING
    /// (or reply) promptly before any slow work.
    /// </summary>
    /// <param name="context">The message, the current task (if any) and the caller.</param>
    /// <param name="sink">Where the agent reports results.</param>
    /// <param name="ct">A token that is canceled when the task is canceled. It is not the caller's request token.</param>
    /// <returns>A task that completes when the agent has reported its last event.</returns>
    ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct);

    /// <summary>Called by CancelTask. The default sets the task to CANCELED.</summary>
    /// <param name="context">The current task and the caller. <see cref="AgentRequestContext.Configuration"/> is <see langword="null"/>.</param>
    /// <param name="sink">Where the agent reports results.</param>
    /// <param name="ct">A token that cancels the cancel call.</param>
    /// <returns>A task that completes when the agent has reported its last event.</returns>
    ValueTask CancelAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
        => sink.UpdateStatusAsync(TaskState.Canceled, null, ct);
}
