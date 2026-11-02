using System.Security.Claims;
using A2A.Protocol;

namespace A2A.Server.Agents;

/// <summary>What an agent gets for one execution.</summary>
/// <param name="AgentName">The agent's catalog name.</param>
/// <param name="TaskId">The task id (already chosen for a new message, even if the agent replies directly).</param>
/// <param name="ContextId">The context id.</param>
/// <param name="Message">The user message, with task and context ids filled in. For cancel: the last user message.</param>
/// <param name="CurrentTask">The stored task for a continuation or cancel; null for a new message.</param>
/// <param name="User">The caller.</param>
/// <param name="ActiveExtensions">Requested extensions that the agent card declares.</param>
/// <param name="Configuration">The request's configuration, if any. Null for cancel.</param>
public sealed record AgentRequestContext(
    string AgentName,
    string TaskId,
    string ContextId,
    Message Message,
    AgentTask? CurrentTask,
    ClaimsPrincipal User,
    IReadOnlyList<string> ActiveExtensions,
    SendMessageConfiguration? Configuration);
