using Ambinder.A2A.Protocol.ProtoMapping;
using Proto = Lf.A2a.V1;

namespace Ambinder.A2A.Protocol.Tests.Grpc;

/// <summary>A client may omit sub-messages; mapping must give defaults (so validation can answer) instead of throwing.</summary>
public class ProtoMapperOmittedFieldsTests
{
    [Fact]
    public void Omitted_message_maps_to_an_empty_message()
    {
        var model = ProtoMapper.FromProto(new Proto.SendMessageRequest());

        Assert.Equal("", model.Message.MessageId);
        Assert.Empty(model.Message.Parts);
    }

    [Fact]
    public void Omitted_status_and_artifact_map_to_defaults()
    {
        Assert.NotNull(ProtoMapper.FromProto(new Proto.Task { Id = "t" }).Status);
        Assert.NotNull(ProtoMapper.FromProto(new Proto.TaskStatusUpdateEvent { TaskId = "t" }).Status);
        Assert.NotNull(ProtoMapper.FromProto(new Proto.TaskArtifactUpdateEvent { TaskId = "t" }).Artifact);
    }
}
