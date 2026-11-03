using System.Net.Http.Headers;
using System.Text;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;
using Grpc.Core;
using Grpc.Net.Client;
using Proto = Lf.A2a.V1;

namespace A2A.Integration.Tests.Conformance;

/// <summary>
/// Spec 3.3.4 / 4.6: a required extension the client did not declare gives ExtensionSupportRequired on every operation, and the
/// activated extensions are echoed in <c>A2A-Extensions</c> (HTTP) / <c>a2a-extensions</c> (gRPC response metadata).
/// </summary>
[Collection(A2ATestHostCollection.Name)]
public sealed class ExtensionNegotiationTests(A2ATestHost host)
{
    private const string AuditExtension = "https://example.com/a2a/ext/audit/v1";
    private const string TimestampExtension = "https://example.com/a2a/ext/timestamp/v1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Missing_required_extension_fails_every_operation(string binding)
    {
        await using var client = await host.CreateClientAsync("secure", binding, TestAuth.Client, ct: Ct);

        async Task Expect(Func<Task> call)
            => Assert.Equal(A2AErrorCode.ExtensionSupportRequired, (await Assert.ThrowsAsync<A2AException>(call)).Code);

        await Expect(() => client.GetTaskAsync(new GetTaskRequest { Id = "any" }, Ct));
        await Expect(() => client.ListTasksAsync(new ListTasksRequest(), Ct));
        await Expect(() => client.CancelTaskAsync(new CancelTaskRequest { Id = "any" }, Ct));
        await Expect(() => client.SendMessageAsync(new SendMessageRequest
        {
            Message = new Message { MessageId = "m", Role = Role.User, Parts = [Part.FromText("x")] },
        }, Ct));
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Required_extension_declared_is_accepted(string binding)
    {
        await using var client = await host.CreateClientAsync("secure", binding, TestAuth.Client, [AuditExtension], Ct);
        var list = await client.ListTasksAsync(new ListTasksRequest(), Ct);
        Assert.Equal("", list.NextPageToken);
    }

    [Theory]
    [InlineData("jsonrpc")]
    [InlineData("rest")]
    public async Task Activated_extensions_are_echoed_in_the_http_header(string transport)
    {
        using var request = transport == "jsonrpc"
            ? new HttpRequestMessage(HttpMethod.Post, $"{A2ATestHost.BaseUrl}/agents/echo/jsonrpc")
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ListTasks","params":{}}""", Encoding.UTF8, A2AConstants.ContentType),
            }
            : new HttpRequestMessage(HttpMethod.Get, $"{A2ATestHost.BaseUrl}/agents/echo/rest/tasks");
        request.Headers.Add(A2AConstants.VersionHeader, "1.0");
        // One declared extension, one the agent does not know: only the declared one is activated.
        request.Headers.Add(A2AConstants.ExtensionsHeader, $"{TimestampExtension}, urn:unknown");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await host.GetTokenAsync(TestAuth.Client, Ct));

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal([TimestampExtension], response.Headers.GetValues(A2AConstants.ExtensionsHeader));
    }

    [Fact]
    public async Task No_header_when_nothing_is_activated()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{A2ATestHost.BaseUrl}/agents/echo/rest/tasks");
        request.Headers.Add(A2AConstants.VersionHeader, "1.0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await host.GetTokenAsync(TestAuth.Client, Ct));

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.True(response.IsSuccessStatusCode);
        Assert.False(response.Headers.Contains(A2AConstants.ExtensionsHeader));
    }

    [Fact]
    public async Task Activated_extensions_are_echoed_in_grpc_metadata()
    {
        var port = A2ATestHost.PortBase + A2ATestHost.GrpcOffsets["echo"];
        using var channel = GrpcChannel.ForAddress($"http://127.0.0.1:{port}");
        var client = new Proto.A2AService.A2AServiceClient(channel);
        var headers = new Metadata
        {
            { A2AConstants.GrpcVersionKey, "1.0" },
            { A2AConstants.GrpcExtensionsKey, $"{TimestampExtension},urn:unknown" },
            { "authorization", $"Bearer {await host.GetTokenAsync(TestAuth.Client, Ct)}" },
        };

        using var call = client.ListTasksAsync(new Proto.ListTasksRequest(), headers, cancellationToken: Ct);
        await call.ResponseAsync;
        var echoed = (await call.ResponseHeadersAsync).GetValue(A2AConstants.GrpcExtensionsKey);

        Assert.Equal(TimestampExtension, echoed);
    }
}
