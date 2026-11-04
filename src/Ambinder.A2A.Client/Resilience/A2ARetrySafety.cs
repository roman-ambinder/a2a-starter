namespace Ambinder.A2A.Client;

/// <summary>Says whether the client may resend a request after a failure.</summary>
/// <remarks>More values can be added in later versions; handle unknown values.</remarks>
public enum A2ARetrySafety
{
    /// <summary>The operation can be repeated without a harmful effect.</summary>
    Idempotent = 0,

    /// <summary>The operation must not be repeated once bytes were sent.</summary>
    NotSafe = 1,
}
