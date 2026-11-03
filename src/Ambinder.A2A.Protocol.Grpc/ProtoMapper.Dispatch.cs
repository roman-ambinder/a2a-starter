using Google.Protobuf;
using Proto = Lf.A2a.V1;

namespace Ambinder.A2A.Protocol.Grpc;

public static partial class ProtoMapper
{
    /// <summary>Maps any top-level Ambinder.A2A.Protocol model to its generated message.</summary>
    public static IMessage ToProtoObject(object model) => model switch
    {
        AgentTask x => ToProto(x),
        AgentTaskStatus x => ToProto(x),
        Message x => ToProto(x),
        Part x => ToProto(x),
        Artifact x => ToProto(x),
        TaskStatusUpdateEvent x => ToProto(x),
        TaskArtifactUpdateEvent x => ToProto(x),
        StreamResponse x => ToProto(x),
        SendMessageResponse x => ToProto(x),
        SendMessageRequest x => ToProto(x),
        SendMessageConfiguration x => ToProto(x),
        GetTaskRequest x => ToProto(x),
        ListTasksRequest x => ToProto(x),
        ListTasksResponse x => ToProto(x),
        CancelTaskRequest x => ToProto(x),
        SubscribeToTaskRequest x => ToProto(x),
        TaskPushNotificationConfig x => ToProto(x),
        AuthenticationInfo x => ToProto(x),
        GetTaskPushNotificationConfigRequest x => ToProto(x),
        DeleteTaskPushNotificationConfigRequest x => ToProto(x),
        ListTaskPushNotificationConfigsRequest x => ToProto(x),
        ListTaskPushNotificationConfigsResponse x => ToProto(x),
        GetExtendedAgentCardRequest x => ToProto(x),
        AgentCard x => ToProto(x),
        AgentInterface x => ToProto(x),
        AgentProvider x => ToProto(x),
        AgentCapabilities x => ToProto(x),
        AgentExtension x => ToProto(x),
        AgentSkill x => ToProto(x),
        AgentCardSignature x => ToProto(x),
        SecurityScheme x => ToProto(x),
        SecurityRequirement x => ToProto(x),
        StringList x => ToProto(x),
        OAuthFlows x => ToProto(x),
        _ => throw new ArgumentException($"No proto mapping for {model.GetType().FullName}.", nameof(model)),
    };

    /// <summary>Maps a generated message back to its Ambinder.A2A.Protocol model.</summary>
    public static object FromProtoObject(IMessage message) => message switch
    {
        Proto.Task x => FromProto(x),
        Proto.TaskStatus x => FromProto(x),
        Proto.Message x => FromProto(x),
        Proto.Part x => FromProto(x),
        Proto.Artifact x => FromProto(x),
        Proto.TaskStatusUpdateEvent x => FromProto(x),
        Proto.TaskArtifactUpdateEvent x => FromProto(x),
        Proto.StreamResponse x => FromProto(x),
        Proto.SendMessageResponse x => FromProto(x),
        Proto.SendMessageRequest x => FromProto(x),
        Proto.SendMessageConfiguration x => FromProto(x),
        Proto.GetTaskRequest x => FromProto(x),
        Proto.ListTasksRequest x => FromProto(x),
        Proto.ListTasksResponse x => FromProto(x),
        Proto.CancelTaskRequest x => FromProto(x),
        Proto.SubscribeToTaskRequest x => FromProto(x),
        Proto.TaskPushNotificationConfig x => FromProto(x),
        Proto.AuthenticationInfo x => FromProto(x),
        Proto.GetTaskPushNotificationConfigRequest x => FromProto(x),
        Proto.DeleteTaskPushNotificationConfigRequest x => FromProto(x),
        Proto.ListTaskPushNotificationConfigsRequest x => FromProto(x),
        Proto.ListTaskPushNotificationConfigsResponse x => FromProto(x),
        Proto.GetExtendedAgentCardRequest x => FromProto(x),
        Proto.AgentCard x => FromProto(x),
        Proto.AgentInterface x => FromProto(x),
        Proto.AgentProvider x => FromProto(x),
        Proto.AgentCapabilities x => FromProto(x),
        Proto.AgentExtension x => FromProto(x),
        Proto.AgentSkill x => FromProto(x),
        Proto.AgentCardSignature x => FromProto(x),
        Proto.SecurityScheme x => FromProto(x),
        Proto.SecurityRequirement x => FromProto(x),
        Proto.StringList x => FromProto(x),
        Proto.OAuthFlows x => FromProto(x),
        _ => throw new ArgumentException($"No model mapping for {message.GetType().FullName}.", nameof(message)),
    };
}
