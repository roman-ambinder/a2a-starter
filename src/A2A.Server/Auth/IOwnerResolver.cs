using System.Security.Claims;
using A2A.Protocol;

namespace A2A.Server.Auth;

/// <summary>
/// Decides who owns a task. Every task operation (get, list, cancel, subscribe, push config) is allowed only for the owner;
/// another caller gets TaskNotFound, so task ids do not leak. Register your own before <c>AddA2AServer</c> to change the rule.
/// </summary>
public interface IOwnerResolver
{
    /// <summary>The owner key for <paramref name="user"/>. Throws <see cref="A2AAuthException"/> (401) when the caller cannot be identified.</summary>
    string GetOwner(ClaimsPrincipal user);

    /// <summary>
    /// True when <paramref name="callerOwner"/> may continue (send a message to) a task owned by <paramref name="taskOwner"/> that is
    /// in <paramref name="state"/>. Only consulted when the two owners differ. The task keeps its original owner.
    /// </summary>
    bool CanContinueInterrupted(string taskOwner, string callerOwner, TaskState state);
}
