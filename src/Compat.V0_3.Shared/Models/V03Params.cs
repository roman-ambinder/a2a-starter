using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ambinder.A2A.Compat.V0_3.Shared.Models;

/// <summary>a2a.json <c>MessageSendParams</c>.</summary>
internal sealed record V03MessageSendParams
{
    public V03Message Message { get; init; } = new();
    public V03MessageSendConfiguration? Configuration { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>a2a.json <c>MessageSendConfiguration</c>.</summary>
internal sealed record V03MessageSendConfiguration
{
    public IReadOnlyList<string>? AcceptedOutputModes { get; init; }
    public int? HistoryLength { get; init; }
    public V03PushNotificationConfig? PushNotificationConfig { get; init; }
    public bool? Blocking { get; init; }

    /// <summary>Read-only alias for <see cref="PushNotificationConfig"/>: the 0.3 proto field is <c>push_notification</c> (a2a.proto <c>SendMessageConfiguration</c>). Never written.</summary>
    [JsonPropertyName("pushNotification")]
    public V03PushNotificationConfig? PushNotification
    {
        get => null;
        init
        {
            if (value is not null)
            {
                PushNotificationConfig = value;
            }
        }
    }
}

/// <summary>a2a.json <c>TaskQueryParams</c>.</summary>
internal sealed record V03TaskQueryParams
{
    public string Id { get; init; } = "";
    public int? HistoryLength { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>a2a.json <c>TaskIdParams</c>.</summary>
internal sealed record V03TaskIdParams
{
    public string Id { get; init; } = "";
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>a2a.json <c>GetTaskPushNotificationConfigParams</c> (<c>pushNotificationConfigId</c> is optional).</summary>
internal sealed record V03GetTaskPushNotificationConfigParams
{
    public string Id { get; init; } = "";
    public string? PushNotificationConfigId { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>a2a.json <c>ListTaskPushNotificationConfigParams</c>.</summary>
internal sealed record V03ListTaskPushNotificationConfigParams
{
    public string Id { get; init; } = "";
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>a2a.json <c>DeleteTaskPushNotificationConfigParams</c>.</summary>
internal sealed record V03DeleteTaskPushNotificationConfigParams
{
    public string Id { get; init; } = "";
    public string PushNotificationConfigId { get; init; } = "";
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}
