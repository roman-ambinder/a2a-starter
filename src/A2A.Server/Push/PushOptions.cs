namespace A2A.Server.Push;

/// <summary>Settings for webhook (push notification) delivery.</summary>
public sealed class PushOptions
{
    /// <summary>Retries after the first failed attempt, so a delivery makes at most <c>1 + MaxRetries</c> attempts.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Wait before the first retry. It doubles each time: 1 s, 2 s, 4 s.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Time limit for one attempt.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Allow plain HTTP and loopback, private and link-local webhook hosts. Development only.</summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>The <c>iss</c> claim of the signature JWT: the server's public base URL. Set by hosting.</summary>
    public string Issuer { get; set; } = "http://localhost";
}
