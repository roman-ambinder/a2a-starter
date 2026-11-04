using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Ambinder.A2A.Protocol;

/// <summary>Source-generated serializer context for every protocol model. Reach it through <see cref="A2AJson"/>.</summary>
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
#pragma warning disable CS0618 // deprecated OAuth flows stay in the context so cards that use them round-trip
[JsonSerializable(typeof(ImplicitOAuthFlow))]
[JsonSerializable(typeof(PasswordOAuthFlow))]
#pragma warning restore CS0618
[JsonSerializable(typeof(ApiKeySecurityScheme))]
[JsonSerializable(typeof(HttpAuthSecurityScheme))]
[JsonSerializable(typeof(MutualTlsSecurityScheme))]
[JsonSerializable(typeof(OAuth2SecurityScheme))]
[JsonSerializable(typeof(OpenIdConnectSecurityScheme))]
[JsonSerializable(typeof(AuthorizationCodeOAuthFlow))]
[JsonSerializable(typeof(ClientCredentialsOAuthFlow))]
[JsonSerializable(typeof(DeviceCodeOAuthFlow))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class A2AJsonContext : JsonSerializerContext;

/// <summary>Shared JSON options matching proto3 canonical JSON.</summary>
public static class A2AJson
{
    /// <summary>
    /// Metadata resolver for every protocol type, with the same rules as <see cref="Options"/>. Add it to
    /// <see cref="JsonTypeInfoResolver.Combine(IJsonTypeInfoResolver[])"/> when your own source-generated context
    /// or an AOT app needs the A2A types.
    /// </summary>
    public static IJsonTypeInfoResolver TypeInfoResolver { get; } = JsonTypeInfoResolver.Combine(A2AJsonContext.Default)
        .WithAddedModifier(OmitEmptyLists);

    /// <summary>Read-only options for the A2A wire format. Empty lists are omitted on write.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(A2AJsonContext.Default.Options)
        {
            TypeInfoResolver = TypeInfoResolver,
        };
        options.MakeReadOnly();
        return options;
    }

    /// <summary>Source-generated metadata for <see cref="AgentTask"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<AgentTask> AgentTask { get; } = (JsonTypeInfo<AgentTask>)Options.GetTypeInfo(typeof(AgentTask));

    /// <summary>Source-generated metadata for <see cref="AgentTaskStatus"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<AgentTaskStatus> AgentTaskStatus { get; } = (JsonTypeInfo<AgentTaskStatus>)Options.GetTypeInfo(typeof(AgentTaskStatus));

    /// <summary>Source-generated metadata for <see cref="TaskState"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<TaskState> TaskState { get; } = (JsonTypeInfo<TaskState>)Options.GetTypeInfo(typeof(TaskState));

    /// <summary>Source-generated metadata for <see cref="Role"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<Role> Role { get; } = (JsonTypeInfo<Role>)Options.GetTypeInfo(typeof(Role));

    /// <summary>Source-generated metadata for <see cref="Message"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<Message> Message { get; } = (JsonTypeInfo<Message>)Options.GetTypeInfo(typeof(Message));

    /// <summary>Source-generated metadata for <see cref="Part"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<Part> Part { get; } = (JsonTypeInfo<Part>)Options.GetTypeInfo(typeof(Part));

    /// <summary>Source-generated metadata for <see cref="Artifact"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<Artifact> Artifact { get; } = (JsonTypeInfo<Artifact>)Options.GetTypeInfo(typeof(Artifact));

    /// <summary>Source-generated metadata for <see cref="TaskStatusUpdateEvent"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<TaskStatusUpdateEvent> TaskStatusUpdateEvent { get; } = (JsonTypeInfo<TaskStatusUpdateEvent>)Options.GetTypeInfo(typeof(TaskStatusUpdateEvent));

    /// <summary>Source-generated metadata for <see cref="TaskArtifactUpdateEvent"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<TaskArtifactUpdateEvent> TaskArtifactUpdateEvent { get; } = (JsonTypeInfo<TaskArtifactUpdateEvent>)Options.GetTypeInfo(typeof(TaskArtifactUpdateEvent));

    /// <summary>Source-generated metadata for <see cref="StreamResponse"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<StreamResponse> StreamResponse { get; } = (JsonTypeInfo<StreamResponse>)Options.GetTypeInfo(typeof(StreamResponse));

    /// <summary>Source-generated metadata for <see cref="SendMessageResponse"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<SendMessageResponse> SendMessageResponse { get; } = (JsonTypeInfo<SendMessageResponse>)Options.GetTypeInfo(typeof(SendMessageResponse));

    /// <summary>Source-generated metadata for <see cref="SendMessageRequest"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<SendMessageRequest> SendMessageRequest { get; } = (JsonTypeInfo<SendMessageRequest>)Options.GetTypeInfo(typeof(SendMessageRequest));

    /// <summary>Source-generated metadata for <see cref="SendMessageConfiguration"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<SendMessageConfiguration> SendMessageConfiguration { get; } = (JsonTypeInfo<SendMessageConfiguration>)Options.GetTypeInfo(typeof(SendMessageConfiguration));

    /// <summary>Source-generated metadata for <see cref="GetTaskRequest"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<GetTaskRequest> GetTaskRequest { get; } = (JsonTypeInfo<GetTaskRequest>)Options.GetTypeInfo(typeof(GetTaskRequest));

    /// <summary>Source-generated metadata for <see cref="ListTasksRequest"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<ListTasksRequest> ListTasksRequest { get; } = (JsonTypeInfo<ListTasksRequest>)Options.GetTypeInfo(typeof(ListTasksRequest));

    /// <summary>Source-generated metadata for <see cref="ListTasksResponse"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<ListTasksResponse> ListTasksResponse { get; } = (JsonTypeInfo<ListTasksResponse>)Options.GetTypeInfo(typeof(ListTasksResponse));

    /// <summary>Source-generated metadata for <see cref="CancelTaskRequest"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<CancelTaskRequest> CancelTaskRequest { get; } = (JsonTypeInfo<CancelTaskRequest>)Options.GetTypeInfo(typeof(CancelTaskRequest));

    /// <summary>Source-generated metadata for <see cref="SubscribeToTaskRequest"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<SubscribeToTaskRequest> SubscribeToTaskRequest { get; } = (JsonTypeInfo<SubscribeToTaskRequest>)Options.GetTypeInfo(typeof(SubscribeToTaskRequest));

    /// <summary>Source-generated metadata for <see cref="TaskPushNotificationConfig"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<TaskPushNotificationConfig> TaskPushNotificationConfig { get; } = (JsonTypeInfo<TaskPushNotificationConfig>)Options.GetTypeInfo(typeof(TaskPushNotificationConfig));

    /// <summary>Source-generated metadata for <see cref="AuthenticationInfo"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<AuthenticationInfo> AuthenticationInfo { get; } = (JsonTypeInfo<AuthenticationInfo>)Options.GetTypeInfo(typeof(AuthenticationInfo));

    /// <summary>Source-generated metadata for <see cref="GetTaskPushNotificationConfigRequest"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<GetTaskPushNotificationConfigRequest> GetTaskPushNotificationConfigRequest { get; } = (JsonTypeInfo<GetTaskPushNotificationConfigRequest>)Options.GetTypeInfo(typeof(GetTaskPushNotificationConfigRequest));

    /// <summary>Source-generated metadata for <see cref="DeleteTaskPushNotificationConfigRequest"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<DeleteTaskPushNotificationConfigRequest> DeleteTaskPushNotificationConfigRequest { get; } = (JsonTypeInfo<DeleteTaskPushNotificationConfigRequest>)Options.GetTypeInfo(typeof(DeleteTaskPushNotificationConfigRequest));

    /// <summary>Source-generated metadata for <see cref="ListTaskPushNotificationConfigsRequest"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<ListTaskPushNotificationConfigsRequest> ListTaskPushNotificationConfigsRequest { get; } = (JsonTypeInfo<ListTaskPushNotificationConfigsRequest>)Options.GetTypeInfo(typeof(ListTaskPushNotificationConfigsRequest));

    /// <summary>Source-generated metadata for <see cref="ListTaskPushNotificationConfigsResponse"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsResponse { get; } = (JsonTypeInfo<ListTaskPushNotificationConfigsResponse>)Options.GetTypeInfo(typeof(ListTaskPushNotificationConfigsResponse));

    /// <summary>Source-generated metadata for <see cref="GetExtendedAgentCardRequest"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<GetExtendedAgentCardRequest> GetExtendedAgentCardRequest { get; } = (JsonTypeInfo<GetExtendedAgentCardRequest>)Options.GetTypeInfo(typeof(GetExtendedAgentCardRequest));

    /// <summary>Source-generated metadata for <see cref="AgentCard"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<AgentCard> AgentCard { get; } = (JsonTypeInfo<AgentCard>)Options.GetTypeInfo(typeof(AgentCard));

    /// <summary>Source-generated metadata for <see cref="AgentInterface"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<AgentInterface> AgentInterface { get; } = (JsonTypeInfo<AgentInterface>)Options.GetTypeInfo(typeof(AgentInterface));

    /// <summary>Source-generated metadata for <see cref="AgentProvider"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<AgentProvider> AgentProvider { get; } = (JsonTypeInfo<AgentProvider>)Options.GetTypeInfo(typeof(AgentProvider));

    /// <summary>Source-generated metadata for <see cref="AgentCapabilities"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<AgentCapabilities> AgentCapabilities { get; } = (JsonTypeInfo<AgentCapabilities>)Options.GetTypeInfo(typeof(AgentCapabilities));

    /// <summary>Source-generated metadata for <see cref="AgentExtension"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<AgentExtension> AgentExtension { get; } = (JsonTypeInfo<AgentExtension>)Options.GetTypeInfo(typeof(AgentExtension));

    /// <summary>Source-generated metadata for <see cref="AgentSkill"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<AgentSkill> AgentSkill { get; } = (JsonTypeInfo<AgentSkill>)Options.GetTypeInfo(typeof(AgentSkill));

    /// <summary>Source-generated metadata for <see cref="AgentCardSignature"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<AgentCardSignature> AgentCardSignature { get; } = (JsonTypeInfo<AgentCardSignature>)Options.GetTypeInfo(typeof(AgentCardSignature));

    /// <summary>Source-generated metadata for <see cref="SecurityScheme"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<SecurityScheme> SecurityScheme { get; } = (JsonTypeInfo<SecurityScheme>)Options.GetTypeInfo(typeof(SecurityScheme));

    /// <summary>Source-generated metadata for <see cref="SecurityRequirement"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<SecurityRequirement> SecurityRequirement { get; } = (JsonTypeInfo<SecurityRequirement>)Options.GetTypeInfo(typeof(SecurityRequirement));

    /// <summary>Source-generated metadata for <see cref="StringList"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<StringList> StringList { get; } = (JsonTypeInfo<StringList>)Options.GetTypeInfo(typeof(StringList));

    /// <summary>Source-generated metadata for <see cref="OAuthFlows"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<OAuthFlows> OAuthFlows { get; } = (JsonTypeInfo<OAuthFlows>)Options.GetTypeInfo(typeof(OAuthFlows));

#pragma warning disable CS0618 // deprecated OAuth flows keep their accessors so cards that use them round-trip
    /// <summary>Source-generated metadata for <see cref="ImplicitOAuthFlow"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<ImplicitOAuthFlow> ImplicitOAuthFlow { get; } = (JsonTypeInfo<ImplicitOAuthFlow>)Options.GetTypeInfo(typeof(ImplicitOAuthFlow));

    /// <summary>Source-generated metadata for <see cref="PasswordOAuthFlow"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<PasswordOAuthFlow> PasswordOAuthFlow { get; } = (JsonTypeInfo<PasswordOAuthFlow>)Options.GetTypeInfo(typeof(PasswordOAuthFlow));
#pragma warning restore CS0618

    /// <summary>Source-generated metadata for <see cref="ApiKeySecurityScheme"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<ApiKeySecurityScheme> ApiKeySecurityScheme { get; } = (JsonTypeInfo<ApiKeySecurityScheme>)Options.GetTypeInfo(typeof(ApiKeySecurityScheme));

    /// <summary>Source-generated metadata for <see cref="HttpAuthSecurityScheme"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<HttpAuthSecurityScheme> HttpAuthSecurityScheme { get; } = (JsonTypeInfo<HttpAuthSecurityScheme>)Options.GetTypeInfo(typeof(HttpAuthSecurityScheme));

    /// <summary>Source-generated metadata for <see cref="MutualTlsSecurityScheme"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<MutualTlsSecurityScheme> MutualTlsSecurityScheme { get; } = (JsonTypeInfo<MutualTlsSecurityScheme>)Options.GetTypeInfo(typeof(MutualTlsSecurityScheme));

    /// <summary>Source-generated metadata for <see cref="OAuth2SecurityScheme"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<OAuth2SecurityScheme> OAuth2SecurityScheme { get; } = (JsonTypeInfo<OAuth2SecurityScheme>)Options.GetTypeInfo(typeof(OAuth2SecurityScheme));

    /// <summary>Source-generated metadata for <see cref="OpenIdConnectSecurityScheme"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<OpenIdConnectSecurityScheme> OpenIdConnectSecurityScheme { get; } = (JsonTypeInfo<OpenIdConnectSecurityScheme>)Options.GetTypeInfo(typeof(OpenIdConnectSecurityScheme));

    /// <summary>Source-generated metadata for <see cref="AuthorizationCodeOAuthFlow"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<AuthorizationCodeOAuthFlow> AuthorizationCodeOAuthFlow { get; } = (JsonTypeInfo<AuthorizationCodeOAuthFlow>)Options.GetTypeInfo(typeof(AuthorizationCodeOAuthFlow));

    /// <summary>Source-generated metadata for <see cref="ClientCredentialsOAuthFlow"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<ClientCredentialsOAuthFlow> ClientCredentialsOAuthFlow { get; } = (JsonTypeInfo<ClientCredentialsOAuthFlow>)Options.GetTypeInfo(typeof(ClientCredentialsOAuthFlow));

    /// <summary>Source-generated metadata for <see cref="DeviceCodeOAuthFlow"/>, using <see cref="Options"/>.</summary>
    public static JsonTypeInfo<DeviceCodeOAuthFlow> DeviceCodeOAuthFlow { get; } = (JsonTypeInfo<DeviceCodeOAuthFlow>)Options.GetTypeInfo(typeof(DeviceCodeOAuthFlow));

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
