using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Tasks;

namespace Ambinder.A2A.Server.Push;

/// <summary>Writes the body of a push notification for one event and one push config.</summary>
/// <remarks>
/// <para>Lifetime: register an implementation as a singleton. The SDK resolves all formatters once and keeps them for the life of the host.</para>
/// <para>Concurrency: <see cref="Format"/> and <see cref="CanFormat"/> are called from many task writes at the same time, so an implementation must be thread-safe and should hold no mutable state.</para>
/// <para><see cref="Format"/> runs inside the task write. It must be fast and must not do network or other I/O. The SDK stores the returned bytes in the outbox and sends the same bytes on every retry. It calls <see cref="Format"/> once per event and payload format, and shares the bytes between configs, so the result must not depend on the config's URL, token or credentials.</para>
/// <para>The formatter is chosen by the protocol version stored with the push config.</para>
/// </remarks>
public interface IPushPayloadFormatter
{
    /// <summary>Says whether this formatter writes bodies for a protocol version.</summary>
    /// <param name="protocolVersion">The protocol version, for example <c>1.0</c>.</param>
    /// <returns><see langword="true"/> when <see cref="Format"/> can write a body for that version.</returns>
    bool CanFormat(string protocolVersion);

    /// <summary>Writes the body bytes and the content type for one event and one config.</summary>
    /// <param name="task">The task after the write that produced the event.</param>
    /// <param name="evt">The event to send.</param>
    /// <param name="config">The push config the notification is for. Do not copy its token or credentials into the body.</param>
    /// <returns>The body bytes and the content type.</returns>
    PushPayload Format(StoredTask task, StreamResponse evt, TaskPushNotificationConfig config);
}

/// <summary>The body and content type of one push notification.</summary>
public readonly record struct PushPayload
{
    /// <summary>The body bytes. They are stored once and sent unchanged on every attempt.</summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>The value of the <c>Content-Type</c> header, for example <c>application/a2a+json</c>.</summary>
    public required string ContentType { get; init; }
}
