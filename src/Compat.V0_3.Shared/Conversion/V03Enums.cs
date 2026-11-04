using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Compat.V0_3.Shared.Conversion;

/// <summary>
/// 0.3 wire strings for <see cref="TaskState"/> and <see cref="Role"/> (a2a.json <c>TaskState</c>, <c>Message.role</c>).
/// An unknown or missing 0.3 string reads as <c>Unspecified</c>; <c>Unspecified</c> writes <c>unknown</c> for a state
/// and no <c>role</c> for a role. See docs/spec-decisions.md, "A2A 0.3 compatibility: field mapping".
/// </summary>
internal static class V03Enums
{
    /// <summary>The 0.3 state string for <see cref="TaskState.Unspecified"/> (a2a.json <c>TaskState</c> has <c>unknown</c>).</summary>
    public const string UnknownState = "unknown";

    /// <summary>Maps a 0.3 state string to a 1.0 state. <c>unknown</c>, an empty string and any string that 0.3 does not define give <see cref="TaskState.Unspecified"/>.</summary>
    public static TaskState ToTaskState(string? state) => state switch
    {
        "submitted" => TaskState.Submitted,
        "working" => TaskState.Working,
        "input-required" => TaskState.InputRequired,
        "completed" => TaskState.Completed,
        "canceled" => TaskState.Canceled,
        "failed" => TaskState.Failed,
        "rejected" => TaskState.Rejected,
        "auth-required" => TaskState.AuthRequired,
        _ => TaskState.Unspecified,
    };

    /// <summary>Maps a 1.0 state to its 0.3 string. <see cref="TaskState.Unspecified"/> and values this version does not know give <c>unknown</c>.</summary>
    public static string ToV03State(TaskState state) => state switch
    {
        TaskState.Submitted => "submitted",
        TaskState.Working => "working",
        TaskState.InputRequired => "input-required",
        TaskState.Completed => "completed",
        TaskState.Canceled => "canceled",
        TaskState.Failed => "failed",
        TaskState.Rejected => "rejected",
        TaskState.AuthRequired => "auth-required",
        _ => UnknownState,
    };

    /// <summary>Maps a 0.3 role string (<c>user</c> or <c>agent</c>) to a 1.0 role. Anything else gives <see cref="Role.Unspecified"/>.</summary>
    public static Role ToRole(string? role) => role switch
    {
        "user" => Role.User,
        "agent" => Role.Agent,
        _ => Role.Unspecified,
    };

    /// <summary>Maps a 1.0 role to its 0.3 string, or <see langword="null"/> for <see cref="Role.Unspecified"/> (the member is then not written).</summary>
    public static string? ToV03Role(Role role) => role switch
    {
        Role.User => "user",
        Role.Agent => "agent",
        _ => null,
    };
}
