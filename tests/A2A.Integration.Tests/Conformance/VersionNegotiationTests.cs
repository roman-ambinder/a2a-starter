using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using A2A.Client;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;
using A2A.Server;
using A2A.Server.Auth;
using A2A.Server.Hosting;
using A2A.Tests.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace A2A.Integration.Tests.Conformance;

/// <summary>
/// Spec 3.6 / design D2: an empty version means A2A 0.3, which the core rejects with VersionNotSupported (ErrorInfo metadata
/// <c>supportedVersions</c> = "1.0") unless a registered <see cref="IA2AVersionDispatcher"/> claims it. A patch part and
/// surrounding spaces are ignored; anything that does not parse as <c>Major.Minor</c> is unsupported. The dispatcher gets the
/// resolved caller and tenant, after auth and before the body is read.
/// </summary>
[Collection(A2ATestHostCollection.Name)]
public sealed class VersionNegotiationTests(A2ATestHost host) : IDisposable
{
    /// <summary>Port base of the extra AgentServer that has a fake dispatcher or a recording handler registered.</summary>
    private static readonly int ExtraPortBase = A2ATestHost.PortBase + 5000;

    private const int DispatcherStatus = 299;
    private const string JsonRpcListTasks = """{"jsonrpc":"2.0","id":1,"method":"ListTasks","params":{}}""";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly List<HttpClient> _clients = [];

    public void Dispose()
    {
        foreach (var client in _clients) client.Dispose();
    }

    // ---------- server: version rules ----------

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Missing_version_is_0_3_and_not_supported(string binding)
    {
        await using var client = await CreateClientAsync(host, A2ATestHost.BaseUrl, binding, version: "");

        var error = await Assert.ThrowsAsync<A2AException>(() => client.ListTasksAsync(new ListTasksRequest(), Ct));

        Assert.Equal(A2AErrorCode.VersionNotSupported, error.Code);
        Assert.Equal("No A2A-Version header was sent, so the request is treated as A2A 0.3. This agent supports 1.0. " +
                     "Send the header 'A2A-Version: 1.0'.", error.Message);
        Assert.Equal("1.0", error.Data!.Value.GetProperty("supportedVersions").GetString());
    }

    public static TheoryData<string, string> UnsupportedVersions()
    {
        var data = new TheoryData<string, string>();
        foreach (var binding in TransportMatrix.Bindings)
            foreach (var version in new[] { "2.0", "abc" })
                data.Add(binding, version);
        return data;
    }

    [Theory, MemberData(nameof(UnsupportedVersions))]
    public async Task Unsupported_versions_are_rejected(string binding, string version)
    {
        await using var client = await CreateClientAsync(host, A2ATestHost.BaseUrl, binding, version);

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
    public async Task Patch_version_is_accepted(string binding, string version)
    {
        await using var client = await CreateClientAsync(host, A2ATestHost.BaseUrl, binding, version);

        var list = await client.ListTasksAsync(new ListTasksRequest(), Ct);

        Assert.NotNull(list);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Negotiated_version_is_major_minor_on_the_call_context(string binding)
    {
        var recorder = new RecordingHandler();
        await using var server = await StartExtraServerAsync(services => services.AddSingleton<IA2ARequestHandler>(recorder));
        await using var client = await CreateClientAsync(host, A2ATestHost.AgentServerBaseUrl(ExtraPortBase), binding, version: "1.0.1");

        await client.ListTasksAsync(new ListTasksRequest(), Ct);

        Assert.Equal("1.0", Assert.Single(recorder.Calls).ProtocolVersion);
    }

    // ---------- client ----------

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Client_sends_the_selected_interface_version(string binding)
    {
        var sent = new ConcurrentQueue<string?>();
        using var http = new HttpClient(new VersionRecorder(sent));
        var url = binding == "GRPC" ? "http://agent.test:5000" : "http://agent.test/a2a";
        var card = new AgentCard
        {
            Name = "agent",
            SupportedInterfaces = [new AgentInterface { Url = url, ProtocolBinding = binding, ProtocolVersion = "1.0.3" }],
        };
        await using var client = A2AClientFactory.Create(card, new A2AClientOptions { HttpClient = http });

        await Assert.ThrowsAnyAsync<Exception>(() => client.ListTasksAsync(new ListTasksRequest(), Ct));

        Assert.Equal("1.0", Assert.Single(sent));
    }

    [Fact]
    public void Card_with_only_0_3_interfaces_says_add_compat()
    {
        var card = new AgentCard
        {
            Name = "agent",
            SupportedInterfaces = [new AgentInterface { Url = "http://agent.test/a2a", ProtocolBinding = "JSONRPC", ProtocolVersion = "0.3" }],
        };

        var error = Assert.Throws<NotSupportedException>(() => A2AClientFactory.Create(card, new A2AClientOptions()));

        Assert.Contains("Ambinder.A2A.Compat.V0_3", error.Message, StringComparison.Ordinal);
    }

    // ---------- telemetry ----------

    [Fact]
    public async Task Version_rejection_sets_error_type_on_the_request_activity()
    {
        using var collector = new ActivityCollector("System.Net.Http", "Microsoft.AspNetCore", A2ATelemetryNames.ServerSource);
        using var request = JsonRpcRequest("echo", JsonRpcListTasks, version: "2.0");

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // JSON-RPC errors travel in the body
        var activity = await collector.WaitForAsync(RequestActivity, TimeSpan.FromSeconds(5));
        Assert.Equal("VERSION_NOT_SUPPORTED", activity.GetTagItem("error.type"));
        Assert.DoesNotContain(collector.Stopped, a => a.Source.Name == A2ATelemetryNames.ServerSource);
    }

    [Fact]
    public async Task Auth_rejection_sets_error_type_on_the_request_activity()
    {
        using var collector = new ActivityCollector("System.Net.Http", "Microsoft.AspNetCore", A2ATelemetryNames.ServerSource);
        using var request = JsonRpcRequest("secure", JsonRpcListTasks, version: "1.0");

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var activity = await collector.WaitForAsync(RequestActivity, TimeSpan.FromSeconds(5));
        Assert.Equal("401", activity.GetTagItem("error.type"));
        Assert.DoesNotContain(collector.Stopped, a => a.Source.Name == A2ATelemetryNames.ServerSource);
    }

    private const string RequestActivity = "Microsoft.AspNetCore.Hosting.HttpRequestIn";

    // ---------- dispatcher seam ----------

    [Fact]
    public async Task JsonRpc_without_version_goes_to_the_dispatcher_with_the_resolved_context()
    {
        var dispatcher = new FakeDispatcher();
        await using var server = await StartExtraServerAsync(services => services.AddSingleton<IA2AVersionDispatcher>(dispatcher));
        using var request = JsonRpcRequest("echo", JsonRpcListTasks, version: null, server: ExtraPortBase);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await host.GetTokenAsync(TestAuth.Client, Ct));

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.Equal(DispatcherStatus, (int)response.StatusCode);
        var seen = Assert.Single(dispatcher.Calls);
        var context = seen.Context;
        Assert.Equal("echo", context.AgentName);
        Assert.Equal("echo", context.PublicCard.Name);
        Assert.Equal("JSONRPC", context.Binding);
        Assert.Equal("", context.RequestedVersion);
        Assert.Equal("", context.Tenant);
        Assert.False(context.RestPath.HasValue);
        Assert.True(context.Caller.User.Identity?.IsAuthenticated);
        var owners = server.Services.GetRequiredService<IOwnerResolver>();
        Assert.Equal(owners.GetOwner(context.Caller.User), context.Caller.Owner);
        Assert.NotEmpty(context.Caller.Owner);
        // D2: the core has not read the body; the dispatcher gets the bytes the client sent.
        Assert.Equal(JsonRpcListTasks, seen.Body);
        Assert.Equal("", await response.Content.ReadAsStringAsync(Ct)); // and the core wrote nothing after it
    }

    [Fact]
    public async Task Rest_path_that_1_0_lacks_reaches_the_dispatcher()
    {
        var dispatcher = new FakeDispatcher();
        await using var server = await StartExtraServerAsync(services => services.AddSingleton<IA2AVersionDispatcher>(dispatcher));
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{A2ATestHost.AgentServerBaseUrl(ExtraPortBase)}/agents/echo/rest/v1/tasks/t1/pushNotificationConfigs");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await host.GetTokenAsync(TestAuth.Client, Ct));

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.Equal(DispatcherStatus, (int)response.StatusCode);
        var context = Assert.Single(dispatcher.Calls).Context;
        Assert.Equal("HTTP+JSON", context.Binding);
        Assert.Equal("/v1/tasks/t1/pushNotificationConfigs", context.RestPath.Value);
        Assert.Equal("echo", context.AgentName);
        Assert.Equal("", context.Tenant);
    }

    [Theory]
    [InlineData("acme/v1/tasks/t1/pushNotificationConfigs", "acme", "/v1/tasks/t1/pushNotificationConfigs")]
    [InlineData("v1/tasks/t1", "", "/v1/tasks/t1")]
    public async Task Rest_tenant_prefix_is_read_only_before_v1(string path, string tenant, string restPath)
    {
        var dispatcher = new FakeDispatcher();
        await using var server = await StartExtraServerAsync(services => services.AddSingleton<IA2AVersionDispatcher>(dispatcher));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{A2ATestHost.AgentServerBaseUrl(ExtraPortBase)}/agents/echo/rest/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await host.GetTokenAsync(TestAuth.Client, Ct));

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.Equal(DispatcherStatus, (int)response.StatusCode);
        var context = Assert.Single(dispatcher.Calls).Context;
        Assert.Equal(tenant, context.Tenant);
        Assert.Equal(restPath, context.RestPath.Value);
    }

    [Fact]
    public async Task Version_1_0_request_to_a_path_1_0_lacks_is_not_dispatched()
    {
        var dispatcher = new FakeDispatcher();
        await using var server = await StartExtraServerAsync(services => services.AddSingleton<IA2AVersionDispatcher>(dispatcher));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{A2ATestHost.AgentServerBaseUrl(ExtraPortBase)}/agents/echo/rest/v1/tasks/t1/nothing-here");
        request.Headers.Add(A2AConstants.VersionHeader, "1.0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await host.GetTokenAsync(TestAuth.Client, Ct));

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.Contains((int)response.StatusCode, new[] { 404, 405 });
        Assert.Empty(dispatcher.Calls);
    }

    [Theory]
    [InlineData("jsonrpc")]
    [InlineData("rest")]
    public async Task Anonymous_request_to_an_agent_requiring_auth_gets_401_and_skips_the_dispatcher(string transport)
    {
        var dispatcher = new FakeDispatcher();
        await using var server = await StartExtraServerAsync(services => services.AddSingleton<IA2AVersionDispatcher>(dispatcher));
        using var request = transport == "jsonrpc"
            ? JsonRpcRequest("secure", JsonRpcListTasks, version: null, server: ExtraPortBase)
            : new HttpRequestMessage(HttpMethod.Get, $"{A2ATestHost.AgentServerBaseUrl(ExtraPortBase)}/agents/secure/rest/tasks");

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(dispatcher.Calls);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Version_the_dispatcher_does_not_claim_is_rejected(string binding)
    {
        var dispatcher = new FakeDispatcher();
        await using var server = await StartExtraServerAsync(services => services.AddSingleton<IA2AVersionDispatcher>(dispatcher));
        await using var client = await CreateClientAsync(host, A2ATestHost.AgentServerBaseUrl(ExtraPortBase), binding, version: "2.0");

        var error = await Assert.ThrowsAsync<A2AException>(() => client.ListTasksAsync(new ListTasksRequest(), Ct));

        Assert.Equal(A2AErrorCode.VersionNotSupported, error.Code);
        Assert.Empty(dispatcher.Calls);
    }

    [Fact]
    public async Task Dispatcher_is_not_asked_for_a_supported_version()
    {
        var dispatcher = new FakeDispatcher();
        await using var server = await StartExtraServerAsync(services => services.AddSingleton<IA2AVersionDispatcher>(dispatcher));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{A2ATestHost.AgentServerBaseUrl(ExtraPortBase)}/agents/echo/rest/tasks");
        request.Headers.Add(A2AConstants.VersionHeader, "1.0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await host.GetTokenAsync(TestAuth.Client, Ct));

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Empty(dispatcher.Calls);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Caller_resolver_returns_what_the_transports_put_on_the_call_context(string binding)
    {
        var recorder = new RecordingHandler();
        await using var server = await StartExtraServerAsync(services => services.AddSingleton<IA2ARequestHandler>(recorder));
        var resolver = server.Services.GetRequiredService<IA2ACallerResolver>();
        var baseUrl = A2ATestHost.AgentServerBaseUrl(ExtraPortBase);
        var card = await host.Cards.GetAgentCardAsync(new Uri($"{baseUrl}/agents/echo"), Ct);

        foreach (var auth in new[] { TestAuth.Client, TestAuth.None })
        {
            await using var client = A2AClientFactory.Create(card, new A2AClientOptions
            {
                HttpClient = host.Http,
                Credentials = host.Credentials(auth, card),
                PreferredBinding = binding,
            });
            await client.ListTasksAsync(new ListTasksRequest(), Ct);
        }

        Assert.Equal(2, recorder.Calls.Count);
        var owners = new List<string>();
        foreach (var call in recorder.Calls)
        {
            var http = new DefaultHttpContext { User = call.User };
            var (caller, tenant) = await resolver.ResolveAsync(http, "echo", call.Tenant, Ct);
            Assert.Equal(call.Owner, caller.Owner);
            Assert.Equal(call.Tenant ?? "", tenant);
            owners.Add(caller.Owner);
        }
        Assert.NotEqual(owners[0], owners[1]); // the authenticated and the anonymous caller are different owners
    }

    // ---------- helpers ----------

    private Task<WebApplication> StartExtraServerAsync(Action<IServiceCollection> register)
        => A2ATestHost.StartAgentServerAsync(ExtraPortBase, A2ATestHost.MockEntraBaseUrl,
            b => b.Host.ConfigureServices((_, services) => register(services)));

    /// <summary>A client over the real card of <paramref name="agent"/> that sends <paramref name="version"/> instead of its own.</summary>
    private async Task<IA2AClient> CreateClientAsync(A2ATestHost testHost, string baseUrl, string binding, string version,
        string agent = "echo")
    {
        var card = await testHost.Cards.GetAgentCardAsync(new Uri($"{baseUrl}/agents/{agent}"), Ct);
        var http = VersionRewriteHandler.CreateClient(version);
        _clients.Add(http);
        return A2AClientFactory.Create(card, new A2AClientOptions
        {
            HttpClient = http,
            Credentials = testHost.Credentials(TestAuth.Client, card),
            PreferredBinding = binding,
        });
    }

    private HttpRequestMessage JsonRpcRequest(string agent, string body, string? version, int? server = null)
    {
        var baseUrl = server is { } port ? A2ATestHost.AgentServerBaseUrl(port) : A2ATestHost.BaseUrl;
        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/agents/{agent}/jsonrpc")
        {
            Content = new StringContent(body, Encoding.UTF8, A2AConstants.ContentType),
        };
        if (version is not null) request.Headers.Add(A2AConstants.VersionHeader, version);
        return request;
    }

    /// <summary>Claims "0.3" and answers HTTP 299; records the context and the request body it saw.</summary>
    private sealed class FakeDispatcher : IA2AVersionDispatcher
    {
        public ConcurrentQueue<(A2AVersionDispatchContext Context, string Body)> Calls { get; } = new();

        public bool CanHandle(string version) => version == "0.3";

        public async Task HandleAsync(HttpContext http, A2AVersionDispatchContext context, CancellationToken ct)
        {
            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
            Calls.Enqueue((context, await reader.ReadToEndAsync(ct)));
            http.Response.StatusCode = DispatcherStatus;
        }
    }

    /// <summary>Records the call context of ListTasks and answers an empty page; every other operation is not used.</summary>
    private sealed class RecordingHandler : IA2ARequestHandler
    {
        public ConcurrentQueue<A2ACallContext> Calls { get; } = new();

        public Task<ListTasksResponse> ListTasksAsync(A2ACallContext call, ListTasksRequest request, CancellationToken ct)
        {
            Calls.Enqueue(call);
            return Task.FromResult(new ListTasksResponse());
        }

        public Task<SendMessageResponse> SendMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(A2ACallContext call, SendMessageRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<AgentTask> GetTaskAsync(A2ACallContext call, GetTaskRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<AgentTask> CancelTaskAsync(A2ACallContext call, CancelTaskRequest request, CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(A2ACallContext call, SubscribeToTaskRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(A2ACallContext call, TaskPushNotificationConfig request, CancellationToken ct) => throw new NotSupportedException();
        public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(A2ACallContext call, GetTaskPushNotificationConfigRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(A2ACallContext call, ListTaskPushNotificationConfigsRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteTaskPushNotificationConfigAsync(A2ACallContext call, DeleteTaskPushNotificationConfigRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<AgentCard> GetExtendedAgentCardAsync(A2ACallContext call, GetExtendedAgentCardRequest request, CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>Records the A2A-Version header of every request and answers 503.</summary>
    private sealed class VersionRecorder(ConcurrentQueue<string?> sent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            sent.Enqueue(request.Headers.TryGetValues(A2AConstants.VersionHeader, out var v) ? v.Single() : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
