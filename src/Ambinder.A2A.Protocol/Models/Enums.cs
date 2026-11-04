using System.Text.Json.Serialization;

namespace Ambinder.A2A.Protocol;

/// <summary>Lifecycle state of a task (proto <c>TaskState</c>).</summary>
/// <remarks>More values can be added in later versions; handle unknown values. An unknown value on the wire reads as <see cref="Unspecified"/>.</remarks>
[JsonConverter(typeof(ProtoEnumConverter<TaskState>))]
public enum TaskState
{
    /// <summary>No state. Servers reject it where a state is required.</summary>
    [JsonStringEnumMemberName("TASK_STATE_UNSPECIFIED")] Unspecified = 0,
    /// <summary>The server has accepted the task. Work has not started.</summary>
    [JsonStringEnumMemberName("TASK_STATE_SUBMITTED")] Submitted = 1,
    /// <summary>The agent is working on the task.</summary>
    [JsonStringEnumMemberName("TASK_STATE_WORKING")] Working = 2,
    /// <summary>The task finished. This is a final state.</summary>
    [JsonStringEnumMemberName("TASK_STATE_COMPLETED")] Completed = 3,
    /// <summary>The task failed. This is a final state.</summary>
    [JsonStringEnumMemberName("TASK_STATE_FAILED")] Failed = 4,
    /// <summary>The task was canceled before it finished. This is a final state.</summary>
    [JsonStringEnumMemberName("TASK_STATE_CANCELED")] Canceled = 5,
    /// <summary>The agent needs more input from the caller. The task is paused.</summary>
    [JsonStringEnumMemberName("TASK_STATE_INPUT_REQUIRED")] InputRequired = 6,
    /// <summary>The agent refused the task. This is a final state.</summary>
    [JsonStringEnumMemberName("TASK_STATE_REJECTED")] Rejected = 7,
    /// <summary>The agent needs the caller to authenticate before it can go on. The task is paused.</summary>
    [JsonStringEnumMemberName("TASK_STATE_AUTH_REQUIRED")] AuthRequired = 8,
}

/// <summary>Sender of a message (proto <c>Role</c>).</summary>
/// <remarks>More values can be added in later versions; handle unknown values. An unknown value on the wire reads as <see cref="Unspecified"/>.</remarks>
[JsonConverter(typeof(ProtoEnumConverter<Role>))]
public enum Role
{
    /// <summary>No role.</summary>
    [JsonStringEnumMemberName("ROLE_UNSPECIFIED")] Unspecified = 0,
    /// <summary>The message comes from the client (the user side).</summary>
    [JsonStringEnumMemberName("ROLE_USER")] User = 1,
    /// <summary>The message comes from the agent.</summary>
    [JsonStringEnumMemberName("ROLE_AGENT")] Agent = 2,
}
