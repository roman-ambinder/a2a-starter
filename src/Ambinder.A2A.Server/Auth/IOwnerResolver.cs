using System.Security.Claims;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Auth;

/// <summary>
/// Decides who owns a task. Every task operation (get, list, cancel, subscribe, push config) is allowed only for the owner;
/// another caller gets TaskNotFound, so task ids do not leak. Register your own before <c>AddA2AServer</c> to change the rule.
/// </summary>
/// <remarks>
/// The resolver is registered as a singleton, so calls can overlap and an implementation must be thread-safe and fast.
/// The owner key must be stable: the same caller must always get the same key, or it loses access to its tasks.
/// </remarks>
public interface IOwnerResolver
{
    /// <summary>The owner key for <paramref name="user"/>. Throws <see cref="A2AAuthException"/> (401) when the caller cannot be identified.</summary>
    /// <param name="user">The caller.</param>
    /// <returns>The owner key.</returns>
    string GetOwner(ClaimsPrincipal user);

    /// <summary>
    /// True when <paramref name="callerOwner"/> may continue (send a message to) a task owned by <paramref name="taskOwner"/> that is
    /// in <paramref name="state"/>. Only consulted when the two owners differ. The task keeps its original owner.
    /// </summary>
    /// <param name="taskOwner">The owner key of the task.</param>
    /// <param name="callerOwner">The owner key of the caller.</param>
    /// <param name="state">The state of the task.</param>
    /// <returns><see langword="true"/> when the caller may continue the task.</returns>
    bool CanContinueInterrupted(string taskOwner, string callerOwner, TaskState state);
}
