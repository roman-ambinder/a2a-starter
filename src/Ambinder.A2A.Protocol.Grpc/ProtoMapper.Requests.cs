using Proto = Lf.A2a.V1;

namespace Ambinder.A2A.Protocol.Grpc;

public static partial class ProtoMapper
{
    public static Proto.SendMessageConfiguration ToProto(SendMessageConfiguration x)
    {
        var c = new Proto.SendMessageConfiguration
        {
            TaskPushNotificationConfig = x.TaskPushNotificationConfig is null ? null : ToProto(x.TaskPushNotificationConfig),
            ReturnImmediately = x.ReturnImmediately,
        };
        if (x.HistoryLength is { } h) c.HistoryLength = h;
        c.AcceptedOutputModes.AddRange(x.AcceptedOutputModes);
        return c;
    }

    public static SendMessageConfiguration FromProto(Proto.SendMessageConfiguration x) => new()
    {
        AcceptedOutputModes = L(x.AcceptedOutputModes),
        TaskPushNotificationConfig = x.TaskPushNotificationConfig is null ? null : FromProto(x.TaskPushNotificationConfig),
        HistoryLength = x.HasHistoryLength ? x.HistoryLength : null,
        ReturnImmediately = x.ReturnImmediately,
    };

    public static Proto.SendMessageRequest ToProto(SendMessageRequest x) => new()
    {
        Tenant = S(x.Tenant), Message = ToProto(x.Message),
        Configuration = x.Configuration is null ? null : ToProto(x.Configuration), Metadata = ToStruct(x.Metadata),
    };

    public static SendMessageRequest FromProto(Proto.SendMessageRequest x) => new()
    {
        Tenant = N(x.Tenant), Message = x.Message is null ? new() : FromProto(x.Message),
        Configuration = x.Configuration is null ? null : FromProto(x.Configuration), Metadata = FromStruct(x.Metadata),
    };

    public static Proto.GetTaskRequest ToProto(GetTaskRequest x)
    {
        var r = new Proto.GetTaskRequest { Tenant = S(x.Tenant), Id = x.Id };
        if (x.HistoryLength is { } h) r.HistoryLength = h;
        return r;
    }

    public static GetTaskRequest FromProto(Proto.GetTaskRequest x) => new()
    {
        Tenant = N(x.Tenant), Id = x.Id, HistoryLength = x.HasHistoryLength ? x.HistoryLength : null,
    };

    public static Proto.ListTasksRequest ToProto(ListTasksRequest x)
    {
        var r = new Proto.ListTasksRequest
        {
            Tenant = S(x.Tenant), ContextId = S(x.ContextId), PageToken = S(x.PageToken),
            StatusTimestampAfter = ToTimestamp(x.StatusTimestampAfter),
        };
        if (x.Status is { } s) r.Status = ToProto(s);
        if (x.PageSize is { } p) r.PageSize = p;
        if (x.HistoryLength is { } h) r.HistoryLength = h;
        if (x.IncludeArtifacts is { } i) r.IncludeArtifacts = i;
        return r;
    }

    public static ListTasksRequest FromProto(Proto.ListTasksRequest x) => new()
    {
        Tenant = N(x.Tenant), ContextId = N(x.ContextId),
        Status = x.Status == Proto.TaskState.Unspecified ? null : FromProto(x.Status),
        PageSize = x.HasPageSize ? x.PageSize : null, PageToken = N(x.PageToken),
        HistoryLength = x.HasHistoryLength ? x.HistoryLength : null,
        StatusTimestampAfter = FromTimestamp(x.StatusTimestampAfter),
        IncludeArtifacts = x.HasIncludeArtifacts ? x.IncludeArtifacts : null,
    };

    public static Proto.CancelTaskRequest ToProto(CancelTaskRequest x) => new()
    {
        Tenant = S(x.Tenant), Id = x.Id, Metadata = ToStruct(x.Metadata),
    };

    public static CancelTaskRequest FromProto(Proto.CancelTaskRequest x) => new()
    {
        Tenant = N(x.Tenant), Id = x.Id, Metadata = FromStruct(x.Metadata),
    };

    public static Proto.SubscribeToTaskRequest ToProto(SubscribeToTaskRequest x) => new() { Tenant = S(x.Tenant), Id = x.Id };

    public static SubscribeToTaskRequest FromProto(Proto.SubscribeToTaskRequest x) => new() { Tenant = N(x.Tenant), Id = x.Id };

    public static Proto.GetTaskPushNotificationConfigRequest ToProto(GetTaskPushNotificationConfigRequest x)
        => new() { Tenant = S(x.Tenant), TaskId = x.TaskId, Id = x.Id };

    public static GetTaskPushNotificationConfigRequest FromProto(Proto.GetTaskPushNotificationConfigRequest x)
        => new() { Tenant = N(x.Tenant), TaskId = x.TaskId, Id = x.Id };

    public static Proto.DeleteTaskPushNotificationConfigRequest ToProto(DeleteTaskPushNotificationConfigRequest x)
        => new() { Tenant = S(x.Tenant), TaskId = x.TaskId, Id = x.Id };

    public static DeleteTaskPushNotificationConfigRequest FromProto(Proto.DeleteTaskPushNotificationConfigRequest x)
        => new() { Tenant = N(x.Tenant), TaskId = x.TaskId, Id = x.Id };

    public static Proto.ListTaskPushNotificationConfigsRequest ToProto(ListTaskPushNotificationConfigsRequest x) => new()
    {
        Tenant = S(x.Tenant), TaskId = x.TaskId, PageSize = x.PageSize ?? 0, PageToken = S(x.PageToken),
    };

    public static ListTaskPushNotificationConfigsRequest FromProto(Proto.ListTaskPushNotificationConfigsRequest x) => new()
    {
        Tenant = N(x.Tenant), TaskId = x.TaskId, PageSize = x.PageSize == 0 ? null : x.PageSize, PageToken = N(x.PageToken),
    };

    public static Proto.GetExtendedAgentCardRequest ToProto(GetExtendedAgentCardRequest x) => new() { Tenant = S(x.Tenant) };

    public static GetExtendedAgentCardRequest FromProto(Proto.GetExtendedAgentCardRequest x) => new() { Tenant = N(x.Tenant) };
}
