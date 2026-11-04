using Ambinder.A2A.Compat.V0_3.Shared.Models;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Compat.V0_3.Shared.Conversion;

/// <summary>
/// Converts the params of the 0.3 methods to the 1.0 request records and back, and the push notification config types.
/// 1.0 <c>Tenant</c> has no 0.3 home: it is dropped on the way out and left <see langword="null"/> on the way in
/// (the dispatcher supplies the tenant from the call context). Members with no 1.0 home are listed in docs/spec-decisions.md.
/// </summary>
internal static class V03RequestConverter
{
    private const string PushConfigSegment = "/pushNotificationConfigs/";

    /// <summary>
    /// <c>message/send</c> and <c>message/stream</c> params to a 1.0 <see cref="SendMessageRequest"/>.
    /// <c>configuration.blocking</c> becomes <c>ReturnImmediately = !blocking</c>. The vendored v0.3.0 files state no default for an absent
    /// <c>blocking</c> (docs/spec-decisions.md, "blocking default"), so an absent <c>blocking</c> and an absent <c>configuration</c> are
    /// treated as <c>false</c>, which gives <c>ReturnImmediately = true</c> (implementer default, awaiting owner review).
    /// </summary>
    public static SendMessageRequest ToV1(V03MessageSendParams parameters)
    {
        if (parameters.Message is null)
        {
            throw new A2AException(A2AErrorCode.InvalidParams, "The params need a 'message'.");
        }

        V03MessageSendConfiguration? configuration = parameters.Configuration;
        return new SendMessageRequest
        {
            Message = V03MessageConverter.ToV1(parameters.Message),
            Configuration = new SendMessageConfiguration
            {
                AcceptedOutputModes = configuration?.AcceptedOutputModes?.ToArray() ?? [],
                HistoryLength = configuration?.HistoryLength,
                TaskPushNotificationConfig = configuration?.PushNotificationConfig is { } push ? ToV1(push, null) : null,
                ReturnImmediately = !(configuration?.Blocking ?? false),
            },
            Metadata = V03MessageConverter.ToV1Metadata(parameters.Metadata),
        };
    }

    /// <summary>
    /// A 1.0 <see cref="SendMessageRequest"/> to 0.3 <c>message/send</c> params. <c>blocking</c> is always written as <c>!ReturnImmediately</c>,
    /// also when <see cref="SendMessageRequest.Configuration"/> is <see langword="null"/> (then <c>blocking</c> is <see langword="true"/>, as the 1.0 default waits).
    /// </summary>
    public static V03MessageSendParams ToV03(SendMessageRequest request)
    {
        SendMessageConfiguration? configuration = request.Configuration;
        return new V03MessageSendParams
        {
            Message = V03MessageConverter.ToV03(request.Message),
            Configuration = new V03MessageSendConfiguration
            {
                AcceptedOutputModes = configuration is null ? null : V03MessageConverter.NullIfEmpty(configuration.AcceptedOutputModes),
                HistoryLength = configuration?.HistoryLength,
                PushNotificationConfig = configuration?.TaskPushNotificationConfig is { } push ? ToV03Config(push) : null,
                Blocking = !(configuration?.ReturnImmediately ?? false),
            },
            Metadata = V03MessageConverter.ToV03Metadata(request.Metadata),
        };
    }

    /// <summary><c>tasks/get</c> params to <see cref="GetTaskRequest"/>. <c>metadata</c> has no 1.0 home and is dropped.</summary>
    public static GetTaskRequest ToV1(V03TaskQueryParams parameters)
        => new() { Id = parameters.Id, HistoryLength = parameters.HistoryLength };

    /// <summary><see cref="GetTaskRequest"/> to <c>tasks/get</c> params.</summary>
    public static V03TaskQueryParams ToV03(GetTaskRequest request)
        => new() { Id = request.Id, HistoryLength = request.HistoryLength };

    /// <summary><c>tasks/cancel</c> params to <see cref="CancelTaskRequest"/>; <c>metadata</c> is carried.</summary>
    public static CancelTaskRequest ToV1Cancel(V03TaskIdParams parameters)
        => new() { Id = parameters.Id, Metadata = V03MessageConverter.ToV1Metadata(parameters.Metadata) };

    /// <summary><see cref="CancelTaskRequest"/> to <c>tasks/cancel</c> params.</summary>
    public static V03TaskIdParams ToV03(CancelTaskRequest request)
        => new() { Id = request.Id, Metadata = V03MessageConverter.ToV03Metadata(request.Metadata) };

    /// <summary><c>tasks/resubscribe</c> params to <see cref="SubscribeToTaskRequest"/>. <c>metadata</c> has no 1.0 home and is dropped.</summary>
    public static SubscribeToTaskRequest ToV1Subscribe(V03TaskIdParams parameters)
        => new() { Id = parameters.Id };

    /// <summary><see cref="SubscribeToTaskRequest"/> to <c>tasks/resubscribe</c> params.</summary>
    public static V03TaskIdParams ToV03(SubscribeToTaskRequest request)
        => new() { Id = request.Id };

    /// <summary>
    /// <c>tasks/pushNotificationConfig/get</c> params to <see cref="GetTaskPushNotificationConfigRequest"/>. The 0.3 <c>pushNotificationConfigId</c>
    /// is optional (a bare <c>TaskIdParams</c> is allowed, a2a.json); 1.0 requires an ID, so an absent one gives <c>Id = ""</c>
    /// and the handler side (Task 13) decides what that means. <c>metadata</c> is dropped.
    /// </summary>
    public static GetTaskPushNotificationConfigRequest ToV1(V03GetTaskPushNotificationConfigParams parameters)
        => new() { TaskId = parameters.Id, Id = parameters.PushNotificationConfigId ?? "" };

    /// <summary>
    /// <see cref="GetTaskPushNotificationConfigRequest"/> to 0.3 params. An empty <c>Id</c> is written as an absent
    /// <c>pushNotificationConfigId</c>, so the two directions invert each other.
    /// </summary>
    public static V03GetTaskPushNotificationConfigParams ToV03(GetTaskPushNotificationConfigRequest request)
        => new() { Id = request.TaskId, PushNotificationConfigId = request.Id.Length == 0 ? null : request.Id };

    /// <summary><c>tasks/pushNotificationConfig/delete</c> params to <see cref="DeleteTaskPushNotificationConfigRequest"/>. <c>metadata</c> is dropped.</summary>
    public static DeleteTaskPushNotificationConfigRequest ToV1(V03DeleteTaskPushNotificationConfigParams parameters)
        => new() { TaskId = parameters.Id, Id = parameters.PushNotificationConfigId };

    /// <summary><see cref="DeleteTaskPushNotificationConfigRequest"/> to 0.3 params.</summary>
    public static V03DeleteTaskPushNotificationConfigParams ToV03(DeleteTaskPushNotificationConfigRequest request)
        => new() { Id = request.TaskId, PushNotificationConfigId = request.Id };

    /// <summary><c>tasks/pushNotificationConfig/list</c> params to <see cref="ListTaskPushNotificationConfigsRequest"/>. 0.3 has no paging: <c>PageSize</c> and <c>PageToken</c> stay <see langword="null"/>. <c>metadata</c> is dropped.</summary>
    public static ListTaskPushNotificationConfigsRequest ToV1(V03ListTaskPushNotificationConfigParams parameters)
        => new() { TaskId = parameters.Id };

    /// <summary><see cref="ListTaskPushNotificationConfigsRequest"/> to 0.3 params. <c>PageSize</c> and <c>PageToken</c> have no 0.3 JSON-RPC home and are dropped.</summary>
    public static V03ListTaskPushNotificationConfigParams ToV03(ListTaskPushNotificationConfigsRequest request)
        => new() { Id = request.TaskId };

    /// <summary>
    /// A 1.0 page of configs to the 0.3 result, a plain array. <see cref="ListTaskPushNotificationConfigsResponse.NextPageToken"/> has no 0.3 JSON-RPC home;
    /// the caller must fetch every page first if it wants the whole list (docs/spec-decisions.md, "Push config list paging").
    /// </summary>
    public static IReadOnlyList<V03TaskPushNotificationConfig> ToV03(ListTaskPushNotificationConfigsResponse response)
        => response.Configs.Select(ToV03).ToArray();

    /// <summary>A 0.3 list result to one 1.0 page without a next page token.</summary>
    public static ListTaskPushNotificationConfigsResponse ToV1(IReadOnlyList<V03TaskPushNotificationConfig> configs)
        => new() { Configs = configs.Select(ToV1).ToArray() };

    /// <summary>
    /// A 0.3 <c>TaskPushNotificationConfig</c> (<c>tasks/pushNotificationConfig/set</c> params and results) to the flat 1.0 type. The task ID comes from
    /// <c>taskId</c>, or from the proto-shaped <c>name</c> (<c>tasks/{id}/pushNotificationConfigs/{id}</c>) when <c>taskId</c> is absent.
    /// </summary>
    public static TaskPushNotificationConfig ToV1(V03TaskPushNotificationConfig config)
    {
        string? taskId = config.TaskId;
        string? nameConfigId = null;
        if (config.Name is { Length: > 0 } name)
        {
            (string? fromNameTask, nameConfigId) = ParseName(name);
            taskId ??= fromNameTask;
        }

        TaskPushNotificationConfig result = ToV1(config.PushNotificationConfig, taskId);
        return result.Id is null && nameConfigId is not null ? result with { Id = nameConfigId } : result;
    }

    /// <summary>A 1.0 push config to the 0.3 <c>TaskPushNotificationConfig</c>. Only <c>taskId</c> is written, not the proto-shaped <c>name</c>. <c>Tenant</c> is dropped.</summary>
    public static V03TaskPushNotificationConfig ToV03(TaskPushNotificationConfig config)
        => new() { TaskId = config.TaskId, PushNotificationConfig = ToV03Config(config) };

    /// <summary>A 0.3 <c>PushNotificationConfig</c> (no task ID of its own) to the 1.0 type.</summary>
    /// <param name="config">The 0.3 config.</param>
    /// <param name="taskId">The task ID to set, or <see langword="null"/> when the task does not exist yet.</param>
    public static TaskPushNotificationConfig ToV1(V03PushNotificationConfig config, string? taskId) => new()
    {
        Id = config.Id,
        TaskId = taskId,
        Url = config.Url,
        Token = config.Token,
        // 0.3 'schemes' is an array, 1.0 'scheme' is one string: the first scheme is kept (implementer default, see docs/spec-decisions.md).
        Authentication = config.Authentication is { } auth
            ? new AuthenticationInfo { Scheme = auth.Schemes is { Count: > 0 } ? auth.Schemes[0] : "", Credentials = auth.Credentials }
            : null,
    };

    /// <summary>A 1.0 push config to the 0.3 <c>PushNotificationConfig</c>. <c>TaskId</c> and <c>Tenant</c> are not part of it.</summary>
    public static V03PushNotificationConfig ToV03Config(TaskPushNotificationConfig config) => new()
    {
        Id = config.Id,
        Url = config.Url,
        Token = config.Token,
        Authentication = config.Authentication is { } auth
            ? new V03PushNotificationAuthenticationInfo
            {
                Schemes = auth.Scheme.Length == 0 ? [] : [auth.Scheme],
                Credentials = auth.Credentials,
            }
            : null,
    };

    private static (string? TaskId, string? ConfigId) ParseName(string name)
    {
        const string prefix = "tasks/";
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return (null, null);
        }

        string rest = name[prefix.Length..];
        int split = rest.IndexOf(PushConfigSegment, StringComparison.Ordinal);
        if (split < 0)
        {
            return (rest.Length == 0 ? null : rest, null);
        }

        string configId = rest[(split + PushConfigSegment.Length)..];
        return (rest[..split], configId.Length == 0 ? null : configId);
    }
}
