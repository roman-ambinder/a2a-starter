namespace Ambinder.A2A.Server.Push;

/// <summary>Settings for webhook (push notification) delivery.</summary>
/// <remarks>Reach the instance through <see cref="Hosting.A2AServerOptions.Push"/>.</remarks>
public sealed class PushOptions
{
    /// <summary>Retries after the first failed attempt, so a delivery makes at most <c>1 + MaxRetries</c> attempts. Default 3.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Wait before the first retry. It doubles each time: 1 s, 2 s, 4 s. Default 1 second.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Time limit for one attempt. Default 15 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Allow plain HTTP and loopback, private and link-local webhook hosts. Development only. Default <see langword="false"/>.</summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>The <c>iss</c> claim of the signature JWT: the server's public base URL. Set by hosting from <see cref="Hosting.A2AServerOptions.PublicBaseUrl"/>, so you normally do not set it.</summary>
    public string Issuer { get; set; } = "http://localhost";
}
