using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Push;

/// <summary>The core default: adds nothing.</summary>
internal sealed class NoOpPushRequestAuthenticator : IPushRequestAuthenticator
{
    public ValueTask AuthenticateAsync(HttpRequestMessage request, TaskPushNotificationConfig config, CancellationToken ct)
        => ValueTask.CompletedTask;
}
