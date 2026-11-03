using Proto = Lf.A2a.V1;

namespace Ambinder.A2A.Protocol.Grpc;

public static partial class ProtoMapper
{
    public static Proto.SendMessageResponse ToProto(SendMessageResponse x)
    {
        var r = new Proto.SendMessageResponse();
        if (x.Task is not null) r.Task = ToProto(x.Task);
        else if (x.Message is not null) r.Message = ToProto(x.Message);
        return r;
    }

    public static SendMessageResponse FromProto(Proto.SendMessageResponse x) => x.PayloadCase switch
    {
        Proto.SendMessageResponse.PayloadOneofCase.Task => new() { Task = FromProto(x.Task) },
        Proto.SendMessageResponse.PayloadOneofCase.Message => new() { Message = FromProto(x.Message) },
        _ => new(),
    };

    public static Proto.StreamResponse ToProto(StreamResponse x)
    {
        var r = new Proto.StreamResponse();
        if (x.Task is not null) r.Task = ToProto(x.Task);
        else if (x.Message is not null) r.Message = ToProto(x.Message);
        else if (x.StatusUpdate is not null) r.StatusUpdate = ToProto(x.StatusUpdate);
        else if (x.ArtifactUpdate is not null) r.ArtifactUpdate = ToProto(x.ArtifactUpdate);
        return r;
    }

    public static StreamResponse FromProto(Proto.StreamResponse x) => x.PayloadCase switch
    {
        Proto.StreamResponse.PayloadOneofCase.Task => new() { Task = FromProto(x.Task) },
        Proto.StreamResponse.PayloadOneofCase.Message => new() { Message = FromProto(x.Message) },
        Proto.StreamResponse.PayloadOneofCase.StatusUpdate => new() { StatusUpdate = FromProto(x.StatusUpdate) },
        Proto.StreamResponse.PayloadOneofCase.ArtifactUpdate => new() { ArtifactUpdate = FromProto(x.ArtifactUpdate) },
        _ => new(),
    };

    public static Proto.ListTasksResponse ToProto(ListTasksResponse x)
    {
        var r = new Proto.ListTasksResponse { NextPageToken = x.NextPageToken, PageSize = x.PageSize, TotalSize = x.TotalSize };
        r.Tasks.AddRange(x.Tasks.Select(ToProto));
        return r;
    }

    public static ListTasksResponse FromProto(Proto.ListTasksResponse x) => new()
    {
        Tasks = L(x.Tasks.Select(FromProto)), NextPageToken = x.NextPageToken, PageSize = x.PageSize, TotalSize = x.TotalSize,
    };

    public static Proto.ListTaskPushNotificationConfigsResponse ToProto(ListTaskPushNotificationConfigsResponse x)
    {
        var r = new Proto.ListTaskPushNotificationConfigsResponse { NextPageToken = S(x.NextPageToken) };
        r.Configs.AddRange(x.Configs.Select(ToProto));
        return r;
    }

    public static ListTaskPushNotificationConfigsResponse FromProto(Proto.ListTaskPushNotificationConfigsResponse x) => new()
    {
        Configs = L(x.Configs.Select(FromProto)), NextPageToken = N(x.NextPageToken),
    };
}
