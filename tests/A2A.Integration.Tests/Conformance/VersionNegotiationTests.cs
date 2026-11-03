using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using A2A.Client;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;
using A2A.Server.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace A2A.Integration.Tests.Conformance;

/// <summary>
/// Spec 3.6 / design D2: an empty version means A2A 0.3, which the core rejects with VersionNotSupported (ErrorInfo metadata
/// <c>supportedVersions</c> = "1.0") unless a registered <see cref="IA2AVersionDispatcher"/> claims it. A patch part and
/// surrounding spaces are ignored; anything that does not parse as <c>Major.Minor</c> is unsupported.
/// </summary>
[Collection(A2ATestHostCollection.Name)]
public sealed class VersionNegotiationTests(A2ATestHost host)
{
    /// <summary>Port base of the extra AgentServer that has a fake dispatcher registered.</summary>
    private static readonly int DispatcherPortBase = A2ATestHost.PortBase + 5000;

    private const int DispatcherStatus = 299;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Missing_version_is_0_3_and_rejected(string binding)
    {
        await using var client = await CreateClientAsync(binding, version: "");

        var error = await Assert.ThrowsAsync<A2AException>(() => client.ListTasksAsync(new ListTasksRequest(), Ct));

        Assert.Equal(A2AErrorCode.VersionNotSupported, error.Code);
        Assert.Contains("treated as A2A 0.3", error.Message, StringComparison.Ordinal);
        Assert.Equal("1.0", error.Data!.Value.GetProperty("supportedVersions").GetString());
    }

    public static TheoryData<string, string> UnsupportedVersions()
    {
        var data = new TheoryData<string, string>();
        foreach (var binding in TransportMatrix.Bindings)
            foreach (var version in new[] { "2.0", "abc", "v1.0" })
                data.Add(binding, version);
        return data;
    }

    [Theory, MemberData(nameof(UnsupportedVersions))]
    public async Task Unsupported_versions_rejected(string binding, string version)
    {
        await using var client = await CreateClientAsync(binding, version);

        var error = await Assert.ThrowsAsync<A2AException>(() => client.ListTasksAsync(new ListTasksRequest(), Ct));

        Assert.Equal(A2AErrorCode.VersionNotSupported, error.Code);
        Assert.Contains($"'{version}' is not supported", error.Message, StringComparison.Ordinal);
        Assert.Equal("1.0", error.Data!.Value.GetProperty("supportedVersions").GetString());
    }

    public static TheoryData<string, string> AcceptedVersions()
    {
        var data = new TheoryData<string, string>();
        foreach (var binding in TransportMatrix.Bindings)
            foreach (var version in new[] { "1.0.1", " 1.0 " })
                data.Add(binding, version);
        return data;
    }

    [Theory, MemberData(nameof(AcceptedVersions))]
    public async Task Patch_and_spaces_accepted(string binding, string version)
    {
        await using var client = await CreateClientAsync(binding, version);

        var list = await client.ListTasksAsync(new ListTasksRequest(), Ct);

        Assert.NotNull(list);
    }

    [Theory]
    [InlineData("jsonrpc")]
    [InlineData("rest")]
    public async Task Registered_dispatcher_receives_empty_version(string transport)
    {
        var dispatcher = new FakeDispatcher();
        await using var server = await A2ATestHost.StartAgentServerAsync(DispatcherPortBase, A2ATestHost.MockEntraBaseUrl,
            b => b.Host.ConfigureServices((_, services) => services.AddSingleton<IA2AVersionDispatcher>(dispatcher)));
        var baseUrl = A2ATestHost.AgentServerBaseUrl(DispatcherPortBase);
        const string body = """{"jsonrpc":"2.0","id":1,"method":"message/send","params":{}}""";
        using var request = transport == "jsonrpc"
            ? new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/agents/echo/jsonrpc")
            {
                Content = new StringContent(body, Encoding.UTF8, A2AConstants.ContentType),
            }
            : new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/agents/echo/rest/v1/message:send")
            {
                Content = new StringContent(body, Encoding.UTF8, A2AConstants.ContentType),
            };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await host.GetTokenAsync(TestAuth.Client, Ct));

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.Equal(DispatcherStatus, (int)response.StatusCode);
        var (agent, version, seenBody) = Assert.Single(dispatcher.Calls);
        Assert.Equal("echo", agent);
        Assert.Equal("0.3", version);
        Assert.Equal(body, seenBody); // the core left the body unread
        Assert.Equal("", await response.Content.ReadAsStringAsync(Ct)); // and wrote nothing after the dispatcher
    }

    [Fact]
    public async Task Dispatcher_is_not_asked_for_a_supported_version()
    {
        var dispatcher = new FakeDispatcher();
        await using var server = await A2ATestHost.StartAgentServerAsync(DispatcherPortBase, A2ATestHost.MockEntraBaseUrl,
            b => b.Host.ConfigureServices((_, services) => services.AddSingleton<IA2AVersionDispatcher>(dispatcher)));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{A2ATestHost.AgentServerBaseUrl(DispatcherPortBase)}/agents/echo/rest/tasks");
        request.Headers.Add(A2AConstants.VersionHeader, "1.0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await host.GetTokenAsync(TestAuth.Client, Ct));

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Empty(dispatcher.Calls);
    }

    private async Task<IA2AClient> CreateClientAsync(string binding, string version)
    {
        var card = await host.GetCardAsync("echo", Ct);
        return A2AClientFactory.Create(card, new A2AClientOptions
        {
            HttpClient = host.Http,
            Credentials = host.Credentials(TestAuth.Client, card),
            PreferredBinding = binding,
            ProtocolVersion = version,
        });
    }

    /// <summary>Claims "0.3" and answers HTTP 299; records agent, version and the request body it saw.</summary>
    private sealed class FakeDispatcher : IA2AVersionDispatcher
    {
        public ConcurrentQueue<(string Agent, string Version, string Body)> Calls { get; } = new();

        public bool CanHandle(string version) => version == "0.3";

        public async Task HandleAsync(HttpContext http, A2AAgentRoute agent, string version, CancellationToken ct)
        {
            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
            Calls.Enqueue((agent.Name, version, await reader.ReadToEndAsync(ct)));
            http.Response.StatusCode = DispatcherStatus;
        }
    }
}
