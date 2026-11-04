using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Tests.WireFixtures;

/// <summary>Maps each proto message to the public .NET model type that carries its JSON body.</summary>
public static class WireTypes
{
    // ImplicitOAuthFlow and PasswordOAuthFlow are [Obsolete] but still on the wire (cards round-trip), so they are mapped on purpose.
#pragma warning disable CS0618
    public static IReadOnlyDictionary<string, Type> ByProtoName { get; } = new Dictionary<string, Type>
    {
        ["SendMessageConfiguration"] = typeof(SendMessageConfiguration),
        ["Task"] = typeof(AgentTask),
        ["TaskStatus"] = typeof(AgentTaskStatus),
        ["Part"] = typeof(Part),
        ["Message"] = typeof(Message),
        ["Artifact"] = typeof(Artifact),
        ["TaskStatusUpdateEvent"] = typeof(TaskStatusUpdateEvent),
        ["TaskArtifactUpdateEvent"] = typeof(TaskArtifactUpdateEvent),
        ["AuthenticationInfo"] = typeof(AuthenticationInfo),
        ["AgentInterface"] = typeof(AgentInterface),
        ["AgentCard"] = typeof(AgentCard),
        ["AgentProvider"] = typeof(AgentProvider),
        ["AgentCapabilities"] = typeof(AgentCapabilities),
        ["AgentExtension"] = typeof(AgentExtension),
        ["AgentSkill"] = typeof(AgentSkill),
        ["AgentCardSignature"] = typeof(AgentCardSignature),
        ["TaskPushNotificationConfig"] = typeof(TaskPushNotificationConfig),
        ["StringList"] = typeof(StringList),
        ["SecurityRequirement"] = typeof(SecurityRequirement),
        ["SecurityScheme"] = typeof(SecurityScheme),
        ["APIKeySecurityScheme"] = typeof(ApiKeySecurityScheme),
        ["HTTPAuthSecurityScheme"] = typeof(HttpAuthSecurityScheme),
        ["OAuth2SecurityScheme"] = typeof(OAuth2SecurityScheme),
        ["OpenIdConnectSecurityScheme"] = typeof(OpenIdConnectSecurityScheme),
        ["MutualTlsSecurityScheme"] = typeof(MutualTlsSecurityScheme),
        ["OAuthFlows"] = typeof(OAuthFlows),
        ["AuthorizationCodeOAuthFlow"] = typeof(AuthorizationCodeOAuthFlow),
        ["ClientCredentialsOAuthFlow"] = typeof(ClientCredentialsOAuthFlow),
        ["ImplicitOAuthFlow"] = typeof(ImplicitOAuthFlow),
        ["PasswordOAuthFlow"] = typeof(PasswordOAuthFlow),
        ["DeviceCodeOAuthFlow"] = typeof(DeviceCodeOAuthFlow),
        ["SendMessageRequest"] = typeof(SendMessageRequest),
        ["GetTaskRequest"] = typeof(GetTaskRequest),
        ["ListTasksRequest"] = typeof(ListTasksRequest),
        ["ListTasksResponse"] = typeof(ListTasksResponse),
        ["CancelTaskRequest"] = typeof(CancelTaskRequest),
        ["GetTaskPushNotificationConfigRequest"] = typeof(GetTaskPushNotificationConfigRequest),
        ["DeleteTaskPushNotificationConfigRequest"] = typeof(DeleteTaskPushNotificationConfigRequest),
        ["SubscribeToTaskRequest"] = typeof(SubscribeToTaskRequest),
        ["ListTaskPushNotificationConfigsRequest"] = typeof(ListTaskPushNotificationConfigsRequest),
        ["GetExtendedAgentCardRequest"] = typeof(GetExtendedAgentCardRequest),
        ["SendMessageResponse"] = typeof(SendMessageResponse),
        ["StreamResponse"] = typeof(StreamResponse),
        ["ListTaskPushNotificationConfigsResponse"] = typeof(ListTaskPushNotificationConfigsResponse),
    };
#pragma warning restore CS0618

    /// <summary>
    /// Proto messages that never appear as a JSON body of their own on any binding, with the reason and where they are covered.
    /// Empty today: every message in proto/a2a.proto has a public model type (request messages are the JSON-RPC params object).
    /// </summary>
    public static IReadOnlyDictionary<string, string> NotStandalone { get; } = new Dictionary<string, string>();
}
