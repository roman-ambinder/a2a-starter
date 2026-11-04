namespace Ambinder.A2A.Server.Push;

/// <summary>Settings for webhook (push notification) delivery. Bound from the configuration section <c>A2A:Push</c>.</summary>
/// <remarks>
/// Reach the instance through <see cref="Hosting.A2AServerOptions.Push"/>. The values are checked when the host starts;
/// each failure message names the option and says what to change.
/// </remarks>
public sealed class PushOptions
{
    /// <summary>Time limit for one delivery attempt. Default 15 seconds. Must be between 1 and 120 seconds; 10 to 30 seconds is a good range.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Wait before the first retry. It grows exponentially with full jitter. Default 1 second.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest wait between attempts. Default 5 minutes. Must not be below <see cref="InitialBackoff"/>.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The most attempts for one delivery, including the first. Default 10. Must be at least 1.</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>A delivery older than this is dead-lettered even if attempts remain. Default 24 hours.</summary>
    public TimeSpan MaxDeliveryAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Honor the <c>Retry-After</c> header of a 429 or 503 answer, capped at <see cref="MaxBackoff"/>. Default <see langword="true"/>.</summary>
    public bool HonorRetryAfter { get; set; } = true;

    /// <summary>How often the dispatcher polls for due deliveries when nothing woke it. Default 1 second.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How long a claimed delivery stays reserved for this instance. Default 60 seconds. Must be more than twice <see cref="Timeout"/>.</summary>
    public TimeSpan ClaimDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>The most deliveries claimed in one call. Default 64. Must be positive.</summary>
    public int BatchSize { get; set; } = 64;

    /// <summary>The most deliveries in flight at once on this instance. Default 32. Must be positive.</summary>
    public int MaxConcurrentDeliveries { get; set; } = 32;

    /// <summary>The most push configs one task may have. More are refused with <c>InvalidParams</c>. Default 10.</summary>
    public int MaxConfigsPerTask { get; set; } = 10;

    /// <summary>The longest webhook URL accepted, in characters. Default 2048.</summary>
    public int MaxUrlLength { get; set; } = 2048;

    /// <summary>Delete a task's push configs after the final delivery for the task. Default <see langword="true"/>.</summary>
    public bool DeleteConfigsAfterFinalDelivery { get; set; } = true;

    /// <summary>How long dead-lettered deliveries are kept before they are purged. Default 7 days.</summary>
    public TimeSpan DeadLetterRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>The most deliveries the in-memory outbox holds. Applies to the in-memory store only. Default 100,000.</summary>
    public int InMemoryOutboxCapacity { get; set; } = 100_000;

    /// <summary>Allow plain HTTP and loopback, private and link-local webhook hosts. Development only. Default <see langword="false"/>.</summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>CIDR ranges (for example <c>10.1.0.0/16</c>) that are allowed even though they are private, such as in-cluster receivers. Default empty.</summary>
    public IList<string> AllowedNetworks { get; } = [];

    /// <summary>If not empty, a webhook host must match one entry: an exact host name, or <c>*.suffix</c>. Default empty, which allows any host.</summary>
    public IList<string> AllowedHosts { get; } = [];

    /// <summary>An absolute proxy URI for deliveries. <see langword="null"/> (the default) means no proxy.</summary>
    public Uri? Proxy { get; set; }

    /// <summary>Use the <c>HTTP(S)_PROXY</c> variables and system proxy settings. Default <see langword="false"/>.</summary>
    public bool UseSystemProxy { get; set; }

    /// <summary>Confirms that the proxy enforces the egress policy. Must be <see langword="true"/> when a proxy is on outside Development. Default <see langword="false"/>.</summary>
    public bool ProxyEnforcesEgressPolicy { get; set; }

    /// <summary>Settings for the optional signature extension.</summary>
    public PushSignatureOptions Signature { get; } = new();

    /// <summary>The time in-flight deliveries get when the host stops. Default 10 seconds. Unfinished claims expire and are picked up later.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The <c>iss</c> claim of the signature JWT: the server's public base URL. Set by hosting from <see cref="Hosting.A2AServerOptions.PublicBaseUrl"/>.</summary>
    internal string Issuer { get; set; } = "http://localhost";
}
