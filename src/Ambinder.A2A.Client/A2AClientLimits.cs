namespace Ambinder.A2A.Client;

/// <summary>Size limits the client applies when it reads from an agent.</summary>
/// <remarks>All properties are set once, at creation. Share one instance between clients.</remarks>
public sealed class A2AClientLimits
{
    /// <summary>Gets the largest unary JSON-RPC or REST response body, in bytes. The default is 8 MiB.</summary>
    public long MaxResponseBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Gets the largest single SSE event (its data lines joined), in bytes. The default is 4 MiB.</summary>
    public int MaxEventBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>Gets the largest gRPC message in either direction, in bytes. The default is 8 MiB.</summary>
    public int MaxGrpcMessageBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Gets the most bytes the client reads from an error body. The default is 64 KiB.</summary>
    public long MaxErrorBodyBytes { get; init; } = 64 * 1024;
}
