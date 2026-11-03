using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Ambinder.A2A.Protocol;

/// <summary>Source-generated serializer context for every protocol model.</summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(UtcTimestampConverter)])]
[JsonSerializable(typeof(AgentTask))]
[JsonSerializable(typeof(AgentTaskStatus))]
[JsonSerializable(typeof(TaskState))]
[JsonSerializable(typeof(Role))]
[JsonSerializable(typeof(Message))]
[JsonSerializable(typeof(Part))]
[JsonSerializable(typeof(Artifact))]
[JsonSerializable(typeof(TaskStatusUpdateEvent))]
[JsonSerializable(typeof(TaskArtifactUpdateEvent))]
[JsonSerializable(typeof(StreamResponse))]
[JsonSerializable(typeof(SendMessageResponse))]
[JsonSerializable(typeof(SendMessageRequest))]
[JsonSerializable(typeof(SendMessageConfiguration))]
[JsonSerializable(typeof(GetTaskRequest))]
[JsonSerializable(typeof(ListTasksRequest))]
[JsonSerializable(typeof(ListTasksResponse))]
[JsonSerializable(typeof(CancelTaskRequest))]
[JsonSerializable(typeof(SubscribeToTaskRequest))]
[JsonSerializable(typeof(TaskPushNotificationConfig))]
[JsonSerializable(typeof(AuthenticationInfo))]
[JsonSerializable(typeof(GetTaskPushNotificationConfigRequest))]
[JsonSerializable(typeof(DeleteTaskPushNotificationConfigRequest))]
[JsonSerializable(typeof(ListTaskPushNotificationConfigsRequest))]
[JsonSerializable(typeof(ListTaskPushNotificationConfigsResponse))]
[JsonSerializable(typeof(GetExtendedAgentCardRequest))]
[JsonSerializable(typeof(AgentCard))]
[JsonSerializable(typeof(AgentInterface))]
[JsonSerializable(typeof(AgentProvider))]
[JsonSerializable(typeof(AgentCapabilities))]
[JsonSerializable(typeof(AgentExtension))]
[JsonSerializable(typeof(AgentSkill))]
[JsonSerializable(typeof(AgentCardSignature))]
[JsonSerializable(typeof(SecurityScheme))]
[JsonSerializable(typeof(SecurityRequirement))]
[JsonSerializable(typeof(StringList))]
[JsonSerializable(typeof(OAuthFlows))]
[JsonSerializable(typeof(ImplicitOAuthFlow))]
[JsonSerializable(typeof(PasswordOAuthFlow))]
[JsonSerializable(typeof(JsonElement))]
public sealed partial class A2AJsonContext : JsonSerializerContext;

/// <summary>Shared JSON options matching proto3 canonical JSON.</summary>
public static class A2AJson
{
    /// <summary>Options backed by <see cref="A2AJsonContext"/>; empty lists are omitted.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(A2AJsonContext.Default.Options)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(A2AJsonContext.Default)
                .WithAddedModifier(OmitEmptyLists),
        };
        options.MakeReadOnly(populateMissingResolver: false);
        return options;
    }

    private static void OmitEmptyLists(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in info.Properties)
        {
            if (property.Get is null || !IsReadOnlyList(property.PropertyType))
            {
                continue;
            }

            property.ShouldSerialize = (_, value) => value is System.Collections.IEnumerable items && HasAny(items);
        }
    }

    private static bool HasAny(System.Collections.IEnumerable items)
    {
        foreach (var _ in items)
        {
            return true;
        }

        return false;
    }

    private static bool IsReadOnlyList(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>);
}
