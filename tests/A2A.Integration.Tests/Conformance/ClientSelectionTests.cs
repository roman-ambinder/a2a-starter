using System.Net;
using A2A.Client;
using A2A.Protocol;

namespace A2A.Integration.Tests.Conformance;

/// <summary>Client side of spec 3.6 (version negotiation) and 8.3.2 (interface selection and tenant).</summary>
public sealed class ClientSelectionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AgentInterface Interface(string binding, string version, string url = "http://agent.test/a2a", string? tenant = null)
        => new() { Url = url, ProtocolBinding = binding, ProtocolVersion = version, Tenant = tenant };

    private static AgentCard Card(params AgentInterface[] interfaces) => new() { Name = "agent", SupportedInterfaces = interfaces };

    private static string TransportOf(IA2AClient client) => ((TracingA2AClient)client).Inner.GetType().Name;

    [Fact]
    public async Task Interfaces_with_an_unsupported_version_are_skipped()
    {
        var card = Card(Interface("JSONRPC", "0.3"), Interface("HTTP+JSON", "1.0"));
        await using var client = A2AClientFactory.Create(card, new A2AClientOptions());
        Assert.Equal("RestA2AClient", TransportOf(client));
    }

    [Fact]
    public async Task Preferred_binding_must_also_have_a_supported_version()
    {
        var card = Card(Interface("GRPC", "2.0", "http://agent.test:5000"), Interface("JSONRPC", "1.0"));
        await using var client = A2AClientFactory.Create(card, new A2AClientOptions { PreferredBinding = "GRPC" });
        Assert.Equal("JsonRpcA2AClient", TransportOf(client));
    }

    [Theory]
    [InlineData("1.0.1")]
    [InlineData("1.0")]
    [InlineData("")]
    public async Task Patch_is_ignored_and_empty_is_accepted(string version)
    {
        await using var client = A2AClientFactory.Create(Card(Interface("JSONRPC", version)), new A2AClientOptions());
        Assert.Equal("JsonRpcA2AClient", TransportOf(client));
    }

    [Fact]
    public void No_supported_version_lists_every_interface()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            A2AClientFactory.Create(Card(Interface("JSONRPC", "0.3"), Interface("HTTP+JSON", "2.0")), new A2AClientOptions()));
        Assert.Contains("JSONRPC/0.3", ex.Message);
        Assert.Contains("HTTP+JSON/2.0", ex.Message);
        Assert.Contains("1.0", ex.Message);
    }

    [Theory]
    [InlineData("grpc.example.com:8443", null, "https://grpc.example.com:8443/")]
    [InlineData("127.0.0.1:5201", null, "http://127.0.0.1:5201/")]
    [InlineData("localhost:5201", null, "http://localhost:5201/")]
    [InlineData("[::1]:5201", null, "http://[::1]:5201/")]
    [InlineData("agent-server:5201", false, "http://agent-server:5201/")]
    [InlineData("localhost:5201", true, "https://localhost:5201/")]
    [InlineData("http://grpc.example.com:8080", true, "http://grpc.example.com:8080/")]
    public void Grpc_host_port_address_gets_a_scheme(string url, bool? useTls, string expected)
        => Assert.Equal(expected, new Uri(A2AClientFactory.GrpcAddress(url, useTls)).ToString());

    [Fact]
    public async Task Grpc_interface_in_host_port_form_creates_a_grpc_client()
    {
        await using var client = A2AClientFactory.Create(Card(Interface("GRPC", "1.0", "localhost:5201")), new A2AClientOptions());
        Assert.Equal("GrpcA2AClient", TransportOf(client));
    }

    [Theory]
    [InlineData("JSONRPC")]
    [InlineData("HTTP+JSON")]
    public async Task Interface_tenant_is_sent_and_a_different_request_tenant_is_refused(string binding)
    {
        var sent = new List<Sent>();
        using var http = new HttpClient(new Recorder(sent));
        var card = Card(Interface(binding, "1.0", tenant: "t1"));
        await using var client = A2AClientFactory.Create(card, new A2AClientOptions { HttpClient = http });

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.GetTaskAsync(new GetTaskRequest { Id = "x", Tenant = "t2" }, Ct));
        Assert.Contains("'t1'", ex.Message);
        Assert.Empty(sent);

        await Assert.ThrowsAnyAsync<Exception>(() => client.GetTaskAsync(new GetTaskRequest { Id = "x" }, Ct));
        var request = Assert.Single(sent);
        if (binding == "HTTP+JSON") Assert.StartsWith("/a2a/t1/tasks/x", request.Path);
        else Assert.Contains("\"tenant\":\"t1\"", request.Body);
        Assert.Equal("1.0", request.Version);
    }

    private sealed record Sent(string Path, string Body, string? Version);

    private sealed class Recorder(List<Sent> sent) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var version = request.Headers.TryGetValues(A2AConstants.VersionHeader, out var v) ? v.Single() : null;
            sent.Add(new Sent(request.RequestUri!.AbsolutePath, body, version));
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }
}
