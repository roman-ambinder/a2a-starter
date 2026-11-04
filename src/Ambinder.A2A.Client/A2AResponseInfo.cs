namespace Ambinder.A2A.Client;

/// <summary>Facts about the response to one client call, passed to <see cref="A2ARequestOptions.OnResponse"/> and to stream wrappers.</summary>
public sealed record A2AResponseInfo
{
    /// <summary>Gets the extensions the agent says it activated for this call (from the <c>A2A-Extensions</c> response header or gRPC metadata).</summary>
    public IReadOnlyList<string> ActivatedExtensions { get; init; } = [];

    /// <summary>Gets the protocol binding that carried the call, for example <c>JSONRPC</c>, <c>HTTP+JSON</c> or <c>GRPC</c>.</summary>
    public required string Transport { get; init; }

    /// <summary>Gets the A2A protocol version of the selected interface.</summary>
    public required string ProtocolVersion { get; init; }

    /// <summary>Gets the HTTP status code of the final response, or <see langword="null"/> on gRPC.</summary>
    public int? HttpStatus { get; init; }

    /// <summary>Gets the gRPC status code name of the final response, or <see langword="null"/> on HTTP.</summary>
    public string? GrpcStatus { get; init; }

    /// <summary>Gets the number of attempts the call took, including the first.</summary>
    public required int Attempts { get; init; }
}
