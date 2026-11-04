namespace Ambinder.A2A.Testing;

/// <summary>The protocol binding a test client uses.</summary>
/// <remarks>More values can be added in later versions; handle unknown values.</remarks>
public enum A2ABinding
{
    /// <summary>JSON-RPC over HTTP.</summary>
    JsonRpc,
    /// <summary>HTTP+JSON (REST).</summary>
    Rest,
    /// <summary>gRPC.</summary>
    Grpc,
}

/// <summary>The bindings a conformance-style test should loop over.</summary>
public static class A2ABindings
{
    /// <summary>JSON-RPC and REST. gRPC is added when the harness supports it.</summary>
    public static IReadOnlyList<A2ABinding> All { get; } = [A2ABinding.JsonRpc, A2ABinding.Rest];
}
