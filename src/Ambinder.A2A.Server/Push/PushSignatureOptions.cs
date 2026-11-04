namespace Ambinder.A2A.Server.Push;

/// <summary>Settings for the optional push signature extension (the <c>X-A2A-Push-Signature</c> JWT).</summary>
/// <remarks>Reach the instance through <see cref="PushOptions.Signature"/>.</remarks>
public sealed class PushSignatureOptions
{
    /// <summary>
    /// Whether to sign deliveries. <see langword="null"/> (the default) means auto: on when a signing key source is configured or
    /// the host runs in Development, off otherwise. <see langword="true"/> or <see langword="false"/> forces the choice.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>How long a signature JWT is valid. Default 5 minutes. Must be between 1 second and 10 minutes, because receivers refuse longer lifetimes.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The JWS algorithm, <c>RS256</c> or <c>ES256</c>. <see langword="null"/> (the default) takes it from the signing key.</summary>
    public string? Algorithm { get; set; }
}
