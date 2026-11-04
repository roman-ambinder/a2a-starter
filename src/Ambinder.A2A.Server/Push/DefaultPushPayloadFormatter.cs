using System.Text.Json;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Tasks;

namespace Ambinder.A2A.Server.Push;

/// <summary>Writes the A2A 1.0 body: the <see cref="StreamResponse"/> as JSON.</summary>
internal sealed class DefaultPushPayloadFormatter : IPushPayloadFormatter
{
    internal const string ContentType = "application/a2a+json";

    public bool CanFormat(string protocolVersion) => protocolVersion == A2AConstants.ProtocolVersion;

    public PushPayload Format(StoredTask task, StreamResponse evt, TaskPushNotificationConfig config)
        => new() { Body = JsonSerializer.SerializeToUtf8Bytes(evt, A2AJson.StreamResponse), ContentType = ContentType };
}
