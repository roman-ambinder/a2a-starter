namespace Ambinder.A2A.Server.Security;

/// <summary>What kind of caller sent a request.</summary>
/// <remarks>More values can be added in later versions; handle unknown values.</remarks>
public enum CallerKind
{
    /// <summary>The request was not authenticated.</summary>
    Anonymous,

    /// <summary>An application called with its own identity (an app-only token).</summary>
    Application,

    /// <summary>A user called through a client app (a delegated token).</summary>
    User,

    /// <summary>A caller authenticated by a scheme this package does not model, for example an API key.</summary>
    Other,
}
