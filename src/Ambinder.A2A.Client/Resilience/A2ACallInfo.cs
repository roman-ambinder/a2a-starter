namespace Ambinder.A2A.Client;

/// <summary>Facts about one client call that the transport and the resilience handlers need.</summary>
/// <remarks>The client creates one instance per call and shares it between that call's attempts. Treat it as read-only.</remarks>
public sealed class A2ACallInfo
{
    /// <summary>The <see cref="HttpRequestMessage.Options"/> key under which transports store the call's <see cref="A2ACallInfo"/>, so the HTTP handlers can read it.</summary>
    public static readonly HttpRequestOptionsKey<A2ACallInfo> Key = new("Ambinder.A2A.Client.CallInfo");

    /// <summary>Gets the operation name, for example <c>SendMessage</c>.</summary>
    public required string Method { get; init; }

    /// <summary>Gets the client name, used in messages.</summary>
    public required string ClientName { get; init; }

    /// <summary>Gets whether the call may be retried after bytes were sent.</summary>
    public required A2ARetrySafety Safety { get; init; }

    /// <summary>Gets the timeout that applies to one attempt of this call.</summary>
    public required TimeSpan Timeout { get; init; }

    /// <summary>Gets the name of the option to raise when <see cref="Timeout"/> is hit, for example <c>A2AClientOptions.Timeouts.Unary</c>.</summary>
    public required string TimeoutOption { get; init; }
}
