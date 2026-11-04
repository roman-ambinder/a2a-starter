using System.Text.Json;
using System.Text.Json.Serialization;
using Ambinder.A2A.Compat.V0_3.Shared.Models;

namespace Ambinder.A2A.Compat.V0_3.Shared.Json;

/// <summary>Source-generated JSON for every 0.3 model. The four <c>kind</c> unions use hand-written converters.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(V03PartConverter), typeof(V03FileContentConverter), typeof(V03ResultConverter), typeof(V03StreamEventConverter)])]
[JsonSerializable(typeof(V03Task))]
[JsonSerializable(typeof(V03TaskStatus))]
[JsonSerializable(typeof(V03Message))]
[JsonSerializable(typeof(V03Artifact))]
[JsonSerializable(typeof(V03Part))]
[JsonSerializable(typeof(V03FileContent))]
[JsonSerializable(typeof(IV03Result))]
[JsonSerializable(typeof(IV03StreamEvent))]
[JsonSerializable(typeof(V03TaskStatusUpdateEvent))]
[JsonSerializable(typeof(V03TaskArtifactUpdateEvent))]
[JsonSerializable(typeof(V03MessageSendParams))]
[JsonSerializable(typeof(V03MessageSendConfiguration))]
[JsonSerializable(typeof(V03TaskQueryParams))]
[JsonSerializable(typeof(V03TaskIdParams))]
[JsonSerializable(typeof(V03GetTaskPushNotificationConfigParams))]
[JsonSerializable(typeof(V03ListTaskPushNotificationConfigParams))]
[JsonSerializable(typeof(V03DeleteTaskPushNotificationConfigParams))]
[JsonSerializable(typeof(V03PushNotificationConfig))]
[JsonSerializable(typeof(V03TaskPushNotificationConfig))]
[JsonSerializable(typeof(IReadOnlyList<V03TaskPushNotificationConfig>))]
[JsonSerializable(typeof(V03AgentCard))]
[JsonSerializable(typeof(V03JsonRpcError))]
[JsonSerializable(typeof(V03ProblemDetails))]
internal sealed partial class V0_3JsonContext : JsonSerializerContext;
