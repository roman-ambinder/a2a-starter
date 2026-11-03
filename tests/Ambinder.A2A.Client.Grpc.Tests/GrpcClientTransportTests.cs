using Ambinder.A2A.Client.Grpc;

namespace Ambinder.A2A.Client.Grpc.Tests;

/// <summary>Internals of the gRPC client transport (the factory-level selection tests are in the integration tests).</summary>
public sealed class GrpcClientTransportTests
{
    [Theory]
    [InlineData("grpc.example.com:8443", null, "https://grpc.example.com:8443/")]
    [InlineData("127.0.0.1:5201", null, "http://127.0.0.1:5201/")]
    [InlineData("localhost:5201", null, "http://localhost:5201/")]
    [InlineData("[::1]:5201", null, "http://[::1]:5201/")]
    [InlineData("agent-server:5201", false, "http://agent-server:5201/")]
    [InlineData("localhost:5201", true, "https://localhost:5201/")]
    [InlineData("http://grpc.example.com:8080", true, "http://grpc.example.com:8080/")]
    public void Grpc_host_port_address_gets_a_scheme(string url, bool? useTls, string expected)
        => Assert.Equal(expected, new Uri(GrpcClientTransport.GrpcAddress(url, useTls)).ToString());
}
