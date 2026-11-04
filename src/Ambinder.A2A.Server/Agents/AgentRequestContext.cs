using System.Security.Claims;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Security;

namespace Ambinder.A2A.Server;

/// <summary>What an agent gets for one execution.</summary>
/// <remarks>The context is a snapshot. It does not change while the agent runs.</remarks>
/// <param name="AgentName">The agent's catalog name.</param>
/// <param name="TaskId">The task id (already chosen for a new message, even if the agent replies directly).</param>
/// <param name="ContextId">The ID of the context (conversation) the task belongs to.</param>
/// <param name="Message">The user message, with task and context ids filled in. For cancel: the last user message.</param>
/// <param name="CurrentTask">The stored task for a continuation or cancel; null for a new message.</param>
/// <param name="User">The caller.</param>
/// <param name="Caller">Who called: owner key, kind and Entra identity. The same value <see cref="IA2ACallerAccessor.Current"/> returns during the run.</param>
/// <param name="ActiveExtensions">Requested extensions that the agent card declares.</param>
/// <param name="Configuration">The request's configuration, if any. Null for cancel.</param>
public sealed record AgentRequestContext(
    string AgentName,
    string TaskId,
    string ContextId,
    Message Message,
    AgentTask? CurrentTask,
    ClaimsPrincipal User,
    A2ACaller Caller,
    IReadOnlyList<string> ActiveExtensions,
    SendMessageConfiguration? Configuration);
