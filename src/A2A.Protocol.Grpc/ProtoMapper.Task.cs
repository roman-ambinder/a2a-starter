using Google.Protobuf;
using Proto = Lf.A2a.V1;

namespace A2A.Protocol.Grpc;

public static partial class ProtoMapper
{
    public static Proto.Part ToProto(Part x)
    {
        var p = new Proto.Part { Filename = S(x.Filename), MediaType = S(x.MediaType), Metadata = ToStruct(x.Metadata) };
        if (x.Text is not null) p.Text = x.Text;
        else if (x.Raw is not null) p.Raw = ByteString.CopyFrom(x.Raw);
        else if (x.Url is not null) p.Url = x.Url;
        else if (x.Data is not null) p.Data = ToValue(x.Data);
        return p;
    }

    public static Part FromProto(Proto.Part x) => new()
    {
        Text = x.ContentCase == Proto.Part.ContentOneofCase.Text ? x.Text : null,
        Raw = x.ContentCase == Proto.Part.ContentOneofCase.Raw ? x.Raw.ToByteArray() : null,
        Url = x.ContentCase == Proto.Part.ContentOneofCase.Url ? x.Url : null,
        Data = x.ContentCase == Proto.Part.ContentOneofCase.Data ? FromValue(x.Data) : null,
        Metadata = FromStruct(x.Metadata),
        Filename = N(x.Filename),
        MediaType = N(x.MediaType),
    };

    public static Proto.Message ToProto(Message x)
    {
        var m = new Proto.Message
        {
            MessageId = x.MessageId, ContextId = S(x.ContextId), TaskId = S(x.TaskId), Role = ToProto(x.Role),
            Metadata = ToStruct(x.Metadata),
        };
        m.Parts.AddRange(x.Parts.Select(ToProto));
        m.Extensions.AddRange(x.Extensions);
        m.ReferenceTaskIds.AddRange(x.ReferenceTaskIds);
        return m;
    }

    public static Message FromProto(Proto.Message x) => new()
    {
        MessageId = x.MessageId, ContextId = N(x.ContextId), TaskId = N(x.TaskId), Role = FromProto(x.Role),
        Parts = L(x.Parts.Select(FromProto)), Metadata = FromStruct(x.Metadata),
        Extensions = L(x.Extensions), ReferenceTaskIds = L(x.ReferenceTaskIds),
    };

    public static Proto.Artifact ToProto(Artifact x)
    {
        var a = new Proto.Artifact
        {
            ArtifactId = x.ArtifactId, Name = S(x.Name), Description = S(x.Description), Metadata = ToStruct(x.Metadata),
        };
        a.Parts.AddRange(x.Parts.Select(ToProto));
        a.Extensions.AddRange(x.Extensions);
        return a;
    }

    public static Artifact FromProto(Proto.Artifact x) => new()
    {
        ArtifactId = x.ArtifactId, Name = N(x.Name), Description = N(x.Description),
        Parts = L(x.Parts.Select(FromProto)), Metadata = FromStruct(x.Metadata), Extensions = L(x.Extensions),
    };

    public static Proto.TaskStatus ToProto(AgentTaskStatus x) => new()
    {
        State = ToProto(x.State),
        Message = x.Message is null ? null : ToProto(x.Message),
        Timestamp = ToTimestamp(x.Timestamp),
    };

    public static AgentTaskStatus FromProto(Proto.TaskStatus x) => new()
    {
        State = FromProto(x.State),
        Message = x.Message is null ? null : FromProto(x.Message),
        Timestamp = FromTimestamp(x.Timestamp),
    };

    public static Proto.Task ToProto(AgentTask x)
    {
        var t = new Proto.Task { Id = x.Id, ContextId = S(x.ContextId), Status = ToProto(x.Status), Metadata = ToStruct(x.Metadata) };
        t.Artifacts.AddRange(x.Artifacts.Select(ToProto));
        t.History.AddRange(x.History.Select(ToProto));
        return t;
    }

    public static AgentTask FromProto(Proto.Task x) => new()
    {
        Id = x.Id, ContextId = N(x.ContextId), Status = x.Status is null ? new() : FromProto(x.Status),
        Artifacts = L(x.Artifacts.Select(FromProto)), History = L(x.History.Select(FromProto)),
        Metadata = FromStruct(x.Metadata),
    };

    public static Proto.TaskStatusUpdateEvent ToProto(TaskStatusUpdateEvent x) => new()
    {
        TaskId = x.TaskId, ContextId = x.ContextId, Status = ToProto(x.Status), Metadata = ToStruct(x.Metadata),
    };

    public static TaskStatusUpdateEvent FromProto(Proto.TaskStatusUpdateEvent x) => new()
    {
        TaskId = x.TaskId, ContextId = x.ContextId, Status = x.Status is null ? new() : FromProto(x.Status), Metadata = FromStruct(x.Metadata),
    };

    public static Proto.TaskArtifactUpdateEvent ToProto(TaskArtifactUpdateEvent x) => new()
    {
        TaskId = x.TaskId, ContextId = x.ContextId, Artifact = ToProto(x.Artifact),
        Append = x.Append, LastChunk = x.LastChunk, Metadata = ToStruct(x.Metadata),
    };

    public static TaskArtifactUpdateEvent FromProto(Proto.TaskArtifactUpdateEvent x) => new()
    {
        TaskId = x.TaskId, ContextId = x.ContextId, Artifact = x.Artifact is null ? new() : FromProto(x.Artifact),
        Append = x.Append, LastChunk = x.LastChunk, Metadata = FromStruct(x.Metadata),
    };
}
