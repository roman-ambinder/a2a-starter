using Ambinder.A2A.AspNetCore.Transports;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Protocol.ProtoMapping;
using Ambinder.A2A.Server;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Hosting;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Proto = Lf.A2a.V1;

namespace Ambinder.A2A.AspNetCore.Grpc;

/// <summary>
/// gRPC binding: a thin adapter over <see cref="IA2ARequestHandler"/>. The agent is chosen by the local port of the connection
/// (<see cref="A2AServerOptions.GrpcPorts"/>; unknown port → NOT_FOUND). Order of checks: agent → agent policy
/// (UNAUTHENTICATED / PERMISSION_DENIED) → <c>a2a-version</c> metadata (FAILED_PRECONDITION) → extended card needs an authenticated
/// caller → map request → required fields (INVALID_ARGUMENT) → handler. Failures are <see cref="RpcException"/>s with a
/// <c>google.rpc.Status</c>; for streams a failure before the first message ends the call with that status, and so does one after it.
/// Caller cancellation ends only this call.
/// </summary>
internal sealed class A2AGrpcService(IA2ARequestHandler handler, AgentCatalog catalog, IOptions<A2AServerOptions> options,
    ILogger<A2AGrpcService> logger) : Proto.A2AService.A2AServiceBase
{
    public override Task<Proto.SendMessageResponse> SendMessage(Proto.SendMessageRequest request, ServerCallContext context)
        => Unary<SendMessageRequest, Proto.SendMessageResponse>(request, context, false,
            async (r, call, ct) => ProtoMapper.ToProto(await handler.SendMessageAsync(call, r, ct).ConfigureAwait(false)));

    public override Task SendStreamingMessage(Proto.SendMessageRequest request, IServerStreamWriter<Proto.StreamResponse> responseStream,
        ServerCallContext context)
        => Stream<SendMessageRequest>(request, responseStream, context, (r, call, ct) => handler.SendStreamingMessageAsync(call, r, ct));

    public override Task<Proto.Task> GetTask(Proto.GetTaskRequest request, ServerCallContext context)
        => Unary<GetTaskRequest, Proto.Task>(request, context, false,
            async (r, call, ct) => ProtoMapper.ToProto(await handler.GetTaskAsync(call, r, ct).ConfigureAwait(false)));

    public override Task<Proto.ListTasksResponse> ListTasks(Proto.ListTasksRequest request, ServerCallContext context)
        => Unary<ListTasksRequest, Proto.ListTasksResponse>(request, context, false,
            async (r, call, ct) => ProtoMapper.ToProto(await handler.ListTasksAsync(call, r, ct).ConfigureAwait(false)));

    public override Task<Proto.Task> CancelTask(Proto.CancelTaskRequest request, ServerCallContext context)
        => Unary<CancelTaskRequest, Proto.Task>(request, context, false,
            async (r, call, ct) => ProtoMapper.ToProto(await handler.CancelTaskAsync(call, r, ct).ConfigureAwait(false)));

    public override Task SubscribeToTask(Proto.SubscribeToTaskRequest request, IServerStreamWriter<Proto.StreamResponse> responseStream,
        ServerCallContext context)
        => Stream<SubscribeToTaskRequest>(request, responseStream, context, (r, call, ct) => handler.SubscribeToTaskAsync(call, r, ct));

    public override Task<Proto.TaskPushNotificationConfig> CreateTaskPushNotificationConfig(Proto.TaskPushNotificationConfig request,
        ServerCallContext context)
        => Unary<TaskPushNotificationConfig, Proto.TaskPushNotificationConfig>(request, context, false,
            async (r, call, ct) => ProtoMapper.ToProto(await handler.CreateTaskPushNotificationConfigAsync(call, r, ct).ConfigureAwait(false)));

    public override Task<Proto.TaskPushNotificationConfig> GetTaskPushNotificationConfig(Proto.GetTaskPushNotificationConfigRequest request,
        ServerCallContext context)
        => Unary<GetTaskPushNotificationConfigRequest, Proto.TaskPushNotificationConfig>(request, context, false,
            async (r, call, ct) => ProtoMapper.ToProto(await handler.GetTaskPushNotificationConfigAsync(call, r, ct).ConfigureAwait(false)));

    public override Task<Proto.ListTaskPushNotificationConfigsResponse> ListTaskPushNotificationConfigs(
        Proto.ListTaskPushNotificationConfigsRequest request, ServerCallContext context)
        => Unary<ListTaskPushNotificationConfigsRequest, Proto.ListTaskPushNotificationConfigsResponse>(request, context, false,
            async (r, call, ct) => ProtoMapper.ToProto(await handler.ListTaskPushNotificationConfigsAsync(call, r, ct).ConfigureAwait(false)));

    public override Task<Empty> DeleteTaskPushNotificationConfig(Proto.DeleteTaskPushNotificationConfigRequest request, ServerCallContext context)
        => Unary<DeleteTaskPushNotificationConfigRequest, Empty>(request, context, false, async (r, call, ct) =>
        {
            await handler.DeleteTaskPushNotificationConfigAsync(call, r, ct).ConfigureAwait(false);
            return new Empty();
        });

    public override Task<Proto.AgentCard> GetExtendedAgentCard(Proto.GetExtendedAgentCardRequest request, ServerCallContext context)
        => Unary<GetExtendedAgentCardRequest, Proto.AgentCard>(request, context, true,
            async (r, call, ct) => ProtoMapper.ToProto(await handler.GetExtendedAgentCardAsync(call, r, ct).ConfigureAwait(false)));

    // ---------- plumbing ----------

    private async Task<TResponse> Unary<TModel, TResponse>(IMessage request, ServerCallContext context, bool isExtendedCard,
        Func<TModel, A2ACallContext, CancellationToken, Task<TResponse>> run) where TModel : notnull
    {
        var ct = context.CancellationToken;
        try
        {
            var (model, call) = await PrepareAsync<TModel>(request, context, isExtendedCard).ConfigureAwait(false);
            var response = await run(model, call, ct).ConfigureAwait(false);
            await CommitMetadataAsync(context).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            await TryCommitMetadataAsync(context).ConfigureAwait(false);
            throw GrpcErrorMapper.Map(ex, logger, ct);
        }
    }

    private async Task Stream<TModel>(IMessage request, IServerStreamWriter<Proto.StreamResponse> writer, ServerCallContext context,
        Func<TModel, A2ACallContext, CancellationToken, IAsyncEnumerable<StreamResponse>> run) where TModel : notnull
    {
        var ct = context.CancellationToken;
        try
        {
            var (model, call) = await PrepareAsync<TModel>(request, context, false).ConfigureAwait(false);
            var first = true;
            await foreach (var evt in run(model, call, ct).WithCancellation(ct).ConfigureAwait(false))
            {
                if (first)
                {
                    first = false;
                    await CommitMetadataAsync(context).ConfigureAwait(false);
                }
                await writer.WriteAsync(ProtoMapper.ToProto(evt), ct).ConfigureAwait(false);
            }
            if (first) await CommitMetadataAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await TryCommitMetadataAsync(context).ConfigureAwait(false);
            throw GrpcErrorMapper.Map(ex, logger, ct);
        }
    }

    private async Task<(TModel Model, A2ACallContext Call)> PrepareAsync<TModel>(IMessage request, ServerCallContext context,
        bool isExtendedCard) where TModel : notnull
    {
        var http = context.GetHttpContext();
        var agent = ResolveAgent(http);
        var (caller, _) = await TransportRequest.ResolveCallerAsync(http, agent, requestTenant: null).ConfigureAwait(false);

        var version = context.RequestHeaders.GetValue(A2AConstants.GrpcVersionKey)?.Trim();
        var extensions = ServiceParameters.ParseExtensions(
            context.RequestHeaders.Where(e => e.Key == A2AConstants.GrpcExtensionsKey && !e.IsBinary).Select(e => e.Value));
        TransportRequest.EnsureVersion(string.IsNullOrEmpty(version) ? null : version);
        if (isExtendedCard)
            TransportRequest.AuthorizeExtendedCard(agent, caller);

        var model = (TModel)ProtoMapper.FromProtoObject(request);
        RequestValidation.Validate(model);
        return (model, TransportRequest.CreateCallContext(http, agent, caller, version, extensions, model, A2ATelemetryNames.TransportGrpc));
    }

    /// <summary>
    /// Sends the allowed response metadata as gRPC response headers (lower-case keys) and marks it committed. Does nothing after the first call.
    /// </summary>
    private static async Task CommitMetadataAsync(ServerCallContext context)
    {
        var http = context.GetHttpContext();
        var metadata = TransportRequest.GetResponseMetadata(http);
        if (!metadata.TryCommit(out var entries)) return;
        var headers = new Metadata();
        foreach (var entry in TransportRequest.AllowedResponseMetadata(http, entries, grpc: true))
            headers.Add(entry.Key.ToLowerInvariant(), entry.Value);
        if (headers.Count > 0) await context.WriteResponseHeadersAsync(headers).ConfigureAwait(false);
    }

    /// <summary>On an error before the commit, the entries still go out as response headers. A failure here never replaces the original error.</summary>
    private static async Task TryCommitMetadataAsync(ServerCallContext context)
    {
        try
        {
            await CommitMetadataAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            // The response headers are already sent or the connection is gone.
        }
    }

    private AgentRegistration ResolveAgent(HttpContext http)
    {
        var port = http.Connection.LocalPort;
        foreach (var (name, agentPort) in options.Value.GrpcPorts)
            if (agentPort == port && catalog.TryGet(name, out var agent))
                return agent;
        throw GrpcErrorMapper.UnknownAgent(port);
    }
}
