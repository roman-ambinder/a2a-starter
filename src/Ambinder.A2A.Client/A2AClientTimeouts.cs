namespace Ambinder.A2A.Client;

/// <summary>Per-operation timeouts for the client.</summary>
/// <remarks>All properties are set once, at creation. Share one instance between clients.</remarks>
public sealed class A2AClientTimeouts
{
    /// <summary>Gets the timeout for unary operations other than a blocking send. The default is 30 seconds.</summary>
    public TimeSpan Unary { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets the timeout for a send with <c>returnImmediately</c> set. The default is 30 seconds.</summary>
    public TimeSpan NonBlockingSend { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets the timeout for a blocking send. The default is 10 minutes.</summary>
    public TimeSpan BlockingSend { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Gets how long a stream may deliver no bytes (heartbeats count) before it times out. The default is 5 minutes.</summary>
    public TimeSpan StreamIdleTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets the timeout for fetching an agent card. The default is 10 seconds.</summary>
    public TimeSpan CardFetch { get; init; } = TimeSpan.FromSeconds(10);
}
