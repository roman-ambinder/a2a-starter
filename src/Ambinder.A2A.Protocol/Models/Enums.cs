using System.Text.Json.Serialization;

namespace Ambinder.A2A.Protocol;

/// <summary>Lifecycle state of a task (proto <c>TaskState</c>).</summary>
[JsonConverter(typeof(ProtoEnumConverter<TaskState>))]
public enum TaskState
{
    [JsonStringEnumMemberName("TASK_STATE_UNSPECIFIED")] Unspecified = 0,
    [JsonStringEnumMemberName("TASK_STATE_SUBMITTED")] Submitted = 1,
    [JsonStringEnumMemberName("TASK_STATE_WORKING")] Working = 2,
    [JsonStringEnumMemberName("TASK_STATE_COMPLETED")] Completed = 3,
    [JsonStringEnumMemberName("TASK_STATE_FAILED")] Failed = 4,
    [JsonStringEnumMemberName("TASK_STATE_CANCELED")] Canceled = 5,
    [JsonStringEnumMemberName("TASK_STATE_INPUT_REQUIRED")] InputRequired = 6,
    [JsonStringEnumMemberName("TASK_STATE_REJECTED")] Rejected = 7,
    [JsonStringEnumMemberName("TASK_STATE_AUTH_REQUIRED")] AuthRequired = 8,
}

/// <summary>Sender of a message (proto <c>Role</c>).</summary>
[JsonConverter(typeof(ProtoEnumConverter<Role>))]
public enum Role
{
    [JsonStringEnumMemberName("ROLE_UNSPECIFIED")] Unspecified = 0,
    [JsonStringEnumMemberName("ROLE_USER")] User = 1,
    [JsonStringEnumMemberName("ROLE_AGENT")] Agent = 2,
}
