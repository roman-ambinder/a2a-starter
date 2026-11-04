using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Ambinder.A2A.AspNetCore.Grpc;
using Ambinder.A2A.Integration.Tests.Fixtures;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Server.Security;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Proto = Lf.A2a.V1;

namespace Ambinder.A2A.Integration.Tests.Security;

/// <summary>
/// Task 3: every transport authenticates once, resolves an <see cref="A2ACaller"/> through <c>IA2ACallerResolver</c> and puts it in
/// <see cref="A2ACallContext.Caller"/>. The raw bearer token is kept only with <c>A2ASecurityOptions.KeepCallerToken</c>.
/// The server listens on ephemeral loopback ports, so no fixed port is used.
/// </summary>
public sealed class CallerResolutionTests
{
    private const string AgentName = "caller";
    private const string Token = "s3cr3t-bearer-token";
    private const string AppOnlyClaims = "tid=t1;azp=app1";
    private const string DelegatedClaims = "tid=t1;azp=app1;oid=user1;scp=read";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string, CallerKind, string?, string?> Claims()
    {
        var data = new TheoryData<string, string, string, CallerKind, string?, string?>();
        foreach (var binding in TransportMatrix.Bindings)
        {
            data.Add(binding, AppOnlyClaims, "tid:t1/app:app1", CallerKind.Application, "app1", null);
            data.Add(binding, DelegatedClaims, "tid:t1/user:user1", CallerKind.User, "app1", "user1");
            data.Add(binding, "", "anonymous", CallerKind.Anonymous, null, null);
        }
        return data;
    }

    [Theory, MemberData(nameof(Claims))]
    public async Task Agent_sees_the_caller_from_the_token_claims(string binding, string claims, string owner, CallerKind kind,
        string? appId, string? userObjectId)
    {
        await using var server = await CallerServer.StartAsync(keepCallerToken: false);

        var call = await server.ListTasksAsync(binding, claims, bearer: Token);

        Assert.Equal(AgentName, call.AgentName);
        Assert.Equal(owner, call.Caller.Owner);
        Assert.Equal(kind, call.Caller.Kind);
        Assert.Equal(claims.Length == 0 ? null : "t1", call.Caller.EntraTenantId);
        Assert.Equal(appId, call.Caller.AppId);
        Assert.Equal(userObjectId, call.Caller.UserObjectId);
        Assert.Equal("", call.Tenant);
        Assert.Equal(claims.Length > 0, call.Caller.Principal.Identity?.IsAuthenticated);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Token_is_dropped_by_default(string binding)
    {
        await using var server = await CallerServer.StartAsync(keepCallerToken: false);

        var call = await server.ListTasksAsync(binding, AppOnlyClaims, bearer: Token);

        Assert.Null(call.Caller.AccessToken);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Token_is_kept_when_configured_under_A2A_Security(string binding)
    {
        await using var server = await CallerServer.StartAsync(keepCallerToken: true);

        var call = await server.ListTasksAsync(binding, AppOnlyClaims, bearer: Token);

        Assert.Equal(Token, call.Caller.AccessToken);
        Assert.Equal("tid:t1/app:app1", call.Caller.Owner);
        Assert.DoesNotContain(Token, call.Caller.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Token, call.ToString(), StringComparison.Ordinal);
    }

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Token_stays_null_without_an_authorization_header_even_when_kept(string binding)
    {
        await using var server = await CallerServer.StartAsync(keepCallerToken: true);

        var call = await server.ListTasksAsync(binding, AppOnlyClaims, bearer: null);

        Assert.Null(call.Caller.AccessToken);
    }

    [Fact]
    public async Task Security_options_are_read_from_the_configuration_section()
    {
        await using var server = await CallerServer.StartAsync(keepCallerToken: false, extraConfig: new Dictionary<string, string?>
        {
            ["A2A:Security:FilterExtendedCardSkills"] = "false",
            ["A2A:Security:Tenants:caller:0"] = "acme",
            ["A2A:Security:Tenants:caller:1"] = "globex",
            ["A2A:Security:TenantBindings:acme:0"] = "tid-a",
        });

        var options = server.App.Services.GetRequiredService<IOptions<A2ASecurityOptions>>().Value;

        Assert.False(options.KeepCallerToken);
        Assert.False(options.FilterExtendedCardSkills);
        Assert.Equal(["acme", "globex"], options.Tenants["caller"]);
        Assert.Equal(["tid-a"], options.TenantBindings["acme"]);
    }

    [Fact]
    public async Task Security_options_default_when_the_section_is_missing()
    {
        await using var server = await CallerServer.StartAsync(keepCallerToken: false);

        var options = server.App.Services.GetRequiredService<IOptions<A2ASecurityOptions>>().Value;

        Assert.False(options.KeepCallerToken);
        Assert.True(options.FilterExtendedCardSkills);
        Assert.Empty(options.Tenants);
        Assert.Empty(options.TenantBindings);
    }

    /// <summary>An A2A server on ephemeral ports whose handler records the call context of ListTasks.</summary>
    private sealed class CallerServer : IAsyncDisposable
    {
        private readonly ConcurrentQueue<A2ACallContext> _calls = new();
        private int _httpPort;
        private int _grpcPort;

        public WebApplication App { get; private set; } = null!;

        public static async Task<CallerServer> StartAsync(bool keepCallerToken, IDictionary<string, string?>? extraConfig = null)
        {
            var server = new CallerServer();
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.Listen(IPAddress.Loopback, 0);
                k.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2);
            });
            var config = new Dictionary<string, string?>();
            if (keepCallerToken) config["A2A:Security:KeepCallerToken"] = "true";
            foreach (var (key, value) in extraConfig ?? new Dictionary<string, string?>()) config[key] = value;
            builder.Configuration.AddInMemoryCollection(config);

            builder.Services.AddAuthentication(HeaderClaimsHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, HeaderClaimsHandler>(HeaderClaimsHandler.SchemeName, null);
            builder.Services.AddSingleton<IA2ARequestHandler>(new ListTasksRecorder(server._calls));
            builder.Services
                .AddA2AServer(o => o.PublicBaseUrl = "http://127.0.0.1:1")
                .AddA2AGrpc()
                .AddAgent<EchoAgent>(AgentName, _ => MinimalServer.Card(AgentName, extended: false));

            server.App = builder.Build();
            server.App.UseAuthentication();
            server.App.MapA2A();
            server.App.MapA2AGrpc();
            await server.App.StartAsync();

            var addresses = server.App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.ToList();
            server._httpPort = new Uri(addresses[0]).Port;
            server._grpcPort = new Uri(addresses[1]).Port;
            // The gRPC service finds its agent by local port, read per request.
            server.App.Services.GetRequiredService<IOptions<A2AServerOptions>>().Value.GrpcPorts[AgentName] = server._grpcPort;
            return server;
        }

        /// <summary>Calls ListTasks on <paramref name="binding"/> and returns the context the handler saw.</summary>
        public async Task<A2ACallContext> ListTasksAsync(string binding, string claims, string? bearer)
        {
            switch (binding)
            {
                case "GRPC":
                    using (var channel = GrpcChannel.ForAddress($"http://127.0.0.1:{_grpcPort}"))
                    {
                        var metadata = new Grpc.Core.Metadata { { A2AConstants.GrpcVersionKey, "1.0" } };
                        if (claims.Length > 0) metadata.Add(HeaderClaimsHandler.Header.ToLowerInvariant(), claims);
                        if (bearer is not null) metadata.Add("authorization", $"Bearer {bearer}");
                        await new Proto.A2AService.A2AServiceClient(channel).ListTasksAsync(new Proto.ListTasksRequest(), metadata, cancellationToken: Ct);
                    }
                    break;
                default:
                    using (var http = new HttpClient())
                    {
                        using var request = binding == "JSONRPC"
                            ? new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_httpPort}/agents/{AgentName}/jsonrpc")
                            {
                                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ListTasks","params":{}}""", Encoding.UTF8, "application/json"),
                            }
                            : new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_httpPort}/agents/{AgentName}/rest/tasks");
                        request.Headers.Add(A2AConstants.VersionHeader, "1.0");
                        if (claims.Length > 0) request.Headers.Add(HeaderClaimsHandler.Header, claims);
                        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                        using var response = await http.SendAsync(request, Ct);
                        response.EnsureSuccessStatusCode();
                    }
                    break;
            }
            return Assert.Single(_calls);
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    /// <summary>Records the call context of ListTasks and answers an empty page; every other operation is not used.</summary>
    private sealed class ListTasksRecorder(ConcurrentQueue<A2ACallContext> calls) : IA2ARequestHandler
    {
        public Task<ListTasksResponse> ListTasksAsync(A2ACallContext call, ListTasksRequest request, CancellationToken ct)
        {
            calls.Enqueue(call);
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

    /// <summary>
    /// Test scheme: <c>X-Test-Claims: type=value;type=value</c> makes an authenticated principal with those claims. No header means
    /// no result (anonymous). The <c>Authorization</c> header is ignored; the transport reads it itself.
    /// </summary>
    private sealed class HeaderClaimsHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "HeaderClaims";
        public const string Header = "X-Test-Claims";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers[Header].ToString();
            if (header.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = header.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .Select(parts => new Claim(parts[0], parts[1]));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
