using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Push;

/// <summary>Adds computed credentials to a push request, after the standard headers are set.</summary>
/// <remarks>
/// Lifetime: registered as a singleton. Concurrency: calls can overlap (many deliveries run at once), so an implementation must be thread-safe.
/// The default does nothing. Do not log header values, and do not perform slow work: the call counts against the delivery timeout.
/// </remarks>
public interface IPushRequestAuthenticator
{
    /// <summary>Called after the standard headers are set. May add or replace headers for this config. Default: no-op.</summary>
    /// <param name="request">The request about to be sent.</param>
    /// <param name="config">The push config the request is delivered for.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>A task that completes when the request is ready to send.</returns>
    ValueTask AuthenticateAsync(HttpRequestMessage request, TaskPushNotificationConfig config, CancellationToken ct);
}
