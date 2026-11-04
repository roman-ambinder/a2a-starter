namespace Ambinder.A2A.Server.Security;

/// <summary>Who may call the ordinary operations of an agent.</summary>
/// <remarks>
/// More values can be added in later versions; handle unknown values. Whatever the mode, the extended agent card is never
/// served to an anonymous caller.
/// </remarks>
public enum AgentAccess
{
    /// <summary>
    /// Every operation except the card endpoints needs an authenticated caller; an unauthenticated one gets 401 with a challenge.
    /// This is the default.
    /// </summary>
    Authenticated,

    /// <summary>
    /// Unauthenticated callers may use the ordinary operations. They are owner <c>anonymous</c>, never list tasks, and reach a
    /// task only by its id. An authenticated caller on the same agent still gets its own owner.
    /// </summary>
    Anonymous,
}
