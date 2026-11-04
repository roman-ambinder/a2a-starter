using System.Runtime.CompilerServices;
using A2A.Protocol;
using A2A.Protocol.Grpc;
using Grpc.Core;
using Grpc.Net.Client;
using Proto = Lf.A2a.V1;

namespace A2A.Client.Transports;

/// <summary>
/// A2A client for the gRPC binding. Protocol version, extensions and credentials travel as call metadata
/// (<c>a2a-version</c>, <c>a2a-extensions</c>, lowercase credential header names). Failures come back as
/// <see cref="A2AException"/> / <see cref="A2AAuthException"/>; caller cancellation as <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class GrpcA2AClient : IA2AClient
{
    private readonly AgentCard _card;
    private readonly A2AClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly Proto.A2AService.A2AServiceClient _client;
    private readonly string? _defaultTenant;
    private readonly string _protocolVersion;

    /// <param name="address">The gRPC address from the card's supportedInterfaces (<c>http://</c> uses h2c).</param>
    /// <param name="card">The agent card; handed to the credential provider.</param>
    /// <param name="defaultTenant">Tenant sent when a request's tenant is empty (the card interface's <c>tenant</c>).</param>
    /// <param name="protocolVersion">The <c>Major.Minor</c> sent as <c>a2a-version</c> (the selected card interface's version). Null → <see cref="A2AConstants.ProtocolVersion"/>.</param>
    public GrpcA2AClient(Uri address, AgentCard card, A2AClientOptions options, string? defaultTenant = null,
        string? protocolVersion = null)
    {
        _protocolVersion = string.IsNullOrWhiteSpace(protocolVersion) ? A2AConstants.ProtocolVersion : protocolVersion;
        _defaultTenant = string.IsNullOrEmpty(defaultTenant) ? null : defaultTenant;
        ArgumentNullException.ThrowIfNull(address);
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        var channelOptions = options.HttpClient is { } http
            ? new GrpcChannelOptions { HttpClient = http }
            : new GrpcChannelOptions { HttpHandler = ClientSupport.CreateHandler(options), DisposeHttpClient = true };
        _channel = GrpcChannel.ForAddress(address, channelOptions);
        _client = new Proto.A2AService.A2AServiceClient(_channel);
    }

    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken ct = default)
    {
        var reply = await UnaryAsync(ProtoMapper.ToProto(ClientSupport.WithTenant(request, _defaultTenant)), (r, o) => _client.SendMessageAsync(r, o), ct).ConfigureAwait(false);
        return Map(() =>
        {
            switch (reply.PayloadCase)
            {
                case Proto.SendMessageResponse.PayloadOneofCase.Task:
                    RequireStatus(reply.Task);
                    break;
                case Proto.SendMessageResponse.PayloadOneofCase.Message:
                    break;
                default:
                    throw ClientSupport.InvalidResponse("SendMessage result must contain exactly one of task or message.");
            }
            return ProtoMapper.FromProto(reply);
        }, "SendMessage result");
    }

    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken ct = default)
        => StreamAsync(ProtoMapper.ToProto(ClientSupport.WithTenant(request, _defaultTenant)), (r, o) => _client.SendStreamingMessage(r, o), ct);

    public async Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken ct = default)
    {
        var reply = await UnaryAsync(ProtoMapper.ToProto(ClientSupport.WithTenant(request, _defaultTenant)), (r, o) => _client.GetTaskAsync(r, o), ct).ConfigureAwait(false);
        return Map(() => ProtoMapper.FromProto(RequireStatus(reply)), "GetTask result");
    }

    public async Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken ct = default)
    {
        var reply = await UnaryAsync(ProtoMapper.ToProto(ClientSupport.WithTenant(request, _defaultTenant)), (r, o) => _client.ListTasksAsync(r, o), ct).ConfigureAwait(false);
        return Map(() =>
        {
            foreach (var task in reply.Tasks) RequireStatus(task);
            return ProtoMapper.FromProto(reply);
        }, "ListTasks result");
    }

    public async Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken ct = default)
    {
        var reply = await UnaryAsync(ProtoMapper.ToProto(ClientSupport.WithTenant(request, _defaultTenant)), (r, o) => _client.CancelTaskAsync(r, o), ct).ConfigureAwait(false);
        return Map(() => ProtoMapper.FromProto(RequireStatus(reply)), "CancelTask result");
    }

    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken ct = default)
        => StreamAsync(ProtoMapper.ToProto(ClientSupport.WithTenant(request, _defaultTenant)), (r, o) => _client.SubscribeToTask(r, o), ct);

    public async Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(TaskPushNotificationConfig request, CancellationToken ct = default)
    {
        var reply = await UnaryAsync(ProtoMapper.ToProto(ClientSupport.WithTenant(request, _defaultTenant)), (r, o) => _client.CreateTaskPushNotificationConfigAsync(r, o), ct).ConfigureAwait(false);
        return Map(() => ProtoMapper.FromProto(reply), "CreateTaskPushNotificationConfig result");
    }

    public async Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken ct = default)
    {
        var reply = await UnaryAsync(ProtoMapper.ToProto(ClientSupport.WithTenant(request, _defaultTenant)), (r, o) => _client.GetTaskPushNotificationConfigAsync(r, o), ct).ConfigureAwait(false);
        return Map(() => ProtoMapper.FromProto(reply), "GetTaskPushNotificationConfig result");
    }

    public async Task<ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigsAsync(ListTaskPushNotificationConfigsRequest request, CancellationToken ct = default)
    {
        var reply = await UnaryAsync(ProtoMapper.ToProto(ClientSupport.WithTenant(request, _defaultTenant)), (r, o) => _client.ListTaskPushNotificationConfigsAsync(r, o), ct).ConfigureAwait(false);
        return Map(() => ProtoMapper.FromProto(reply), "ListTaskPushNotificationConfigs result");
    }

    public async Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken ct = default)
        => _ = await UnaryAsync(ProtoMapper.ToProto(ClientSupport.WithTenant(request, _defaultTenant)), (r, o) => _client.DeleteTaskPushNotificationConfigAsync(r, o), ct).ConfigureAwait(false);

    public async Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken ct = default)
    {
        var reply = await UnaryAsync(ProtoMapper.ToProto(ClientSupport.WithTenant(request, _defaultTenant)), (r, o) => _client.GetExtendedAgentCardAsync(r, o), ct).ConfigureAwait(false);
        return Map(() => ProtoMapper.FromProto(reply), "GetExtendedAgentCard result");
    }

    public ValueTask DisposeAsync()
    {
        _channel.Dispose();
        return ValueTask.CompletedTask;
    }

    // ---------- plumbing ----------

    private async Task<TResponse> UnaryAsync<TRequest, TResponse>(TRequest request,
        Func<TRequest, CallOptions, AsyncUnaryCall<TResponse>> call, CancellationToken ct)
    {
        var options = new CallOptions(await BuildMetadataAsync(ct).ConfigureAwait(false), cancellationToken: ct);
        try
        {
            using var running = call(request, options);
            return await running.ResponseAsync.ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            throw Failure(ex, ct);
        }
    }

    private async IAsyncEnumerable<StreamResponse> StreamAsync<TRequest>(TRequest request,
        Func<TRequest, CallOptions, AsyncServerStreamingCall<Proto.StreamResponse>> start,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var options = new CallOptions(await BuildMetadataAsync(ct).ConfigureAwait(false), cancellationToken: ct);
        using var call = start(request, options); // disposing it (early exit) cancels the RPC quietly
        var final = false;
        while (true)
        {
            Proto.StreamResponse message;
            try
            {
                if (!await call.ResponseStream.MoveNext(ct).ConfigureAwait(false)) break;
                message = call.ResponseStream.Current;
            }
            catch (RpcException ex)
            {
                throw Failure(ex, ct);
            }

            var evt = ClientSupport.Validate(Map(() => FromProto(message), "stream event"));
            final |= ClientSupport.IsFinal(evt);
            yield return evt;
        }
        if (!final) throw ClientSupport.StreamEndedEarly();
    }

    private static StreamResponse FromProto(Proto.StreamResponse message)
    {
        switch (message.PayloadCase)
        {
            case Proto.StreamResponse.PayloadOneofCase.Task:
                RequireStatus(message.Task);
                break;
            case Proto.StreamResponse.PayloadOneofCase.StatusUpdate:
                if (message.StatusUpdate.Status is null) throw ClientSupport.InvalidResponse("A status update has no status.");
                break;
            case Proto.StreamResponse.PayloadOneofCase.ArtifactUpdate:
                if (message.ArtifactUpdate.Artifact is null) throw ClientSupport.InvalidResponse("An artifact update has no artifact.");
                break;
            case Proto.StreamResponse.PayloadOneofCase.Message:
                break;
            default:
                throw ClientSupport.InvalidResponse("A stream event must contain exactly one payload.");
        }
        return ProtoMapper.FromProto(message);
    }

    private static Proto.Task RequireStatus(Proto.Task task)
        => task.Status is null ? throw ClientSupport.InvalidResponse("The task has no status.") : task;

    /// <summary>Runs a proto → model mapping; anything but an A2A error becomes InvalidAgentResponse.</summary>
    private static T Map<T>(Func<T> map, string what)
    {
        try
        {
            return map();
        }
        catch (Exception ex) when (ex is not A2AException and not OperationCanceledException)
        {
            throw ClientSupport.InvalidResponse($"The {what} is not valid.", ex);
        }
    }

    private static Exception Failure(RpcException ex, CancellationToken ct)
        => ct.IsCancellationRequested && ex.StatusCode == StatusCode.Cancelled
            ? new OperationCanceledException(ct)
            : GrpcErrorTranslator.Translate(ex);

    private async Task<Metadata> BuildMetadataAsync(CancellationToken ct)
    {
        var metadata = new Metadata { { A2AConstants.GrpcVersionKey, _protocolVersion } };
        if (_options.Extensions.Count > 0)
            metadata.Add(A2AConstants.GrpcExtensionsKey, string.Join(',', _options.Extensions));

        if (_options.Credentials is { } credentials)
        {
            var headers = await credentials.GetHeadersAsync(_card, ct).ConfigureAwait(false);
            foreach (var (name, value) in headers)
            {
                try
                {
                    metadata.Add(name.ToLowerInvariant(), value);
                }
                catch (ArgumentException)
                {
                    // Never echo the value: it is a credential.
                    throw new ArgumentException($"The header '{name}' cannot be sent as gRPC metadata.", nameof(name));
                }
            }
        }
        return metadata;
    }
}
