namespace Ambinder.A2A.Integration.Tests.Fixtures;

/// <summary>The three protocol bindings every client/server integration test runs against.</summary>
public static class TransportMatrix
{
    public static TheoryData<string> Bindings => ["JSONRPC", "HTTP+JSON", "GRPC"];
}
