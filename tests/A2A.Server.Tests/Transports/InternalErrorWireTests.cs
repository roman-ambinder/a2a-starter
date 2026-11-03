using System.Text.Json;
using System.Text.Json.Nodes;
using A2A.Protocol;
using A2A.Server.Transports;
using A2A.Server.Transports.Grpc;
using A2A.Server.Transports.Rest;
using Google.Rpc;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace A2A.Server.Tests.Transports;

/// <summary>
/// Only the server-generated internal error (unknown exception, with a trace id) carries an ErrorInfo. An
/// <see cref="A2AErrorCode.InternalError"/> a handler throws with its own data keeps that data off the wire.
/// </summary>
public class InternalErrorWireTests
{
    private static A2AException Generated() => TransportErrors.ToA2AException(new InvalidOperationException("secret"), NullLogger.Instance);

    private static A2AException FromHandler()
        => new(A2AErrorCode.InternalError, "handler said so", JsonSerializer.SerializeToElement(new { k = "v" }));

    [Fact]
    public void Generated_error_carries_reason_and_trace_id_on_rest()
    {
        var error = Generated();
        var info = JsonNode.Parse(RestErrorWriter.ToJson(error))!["error"]!["details"]![0]!;

        Assert.Equal("INTERNAL", info["reason"]!.GetValue<string>());
        Assert.Equal("a2a-protocol.org", info["domain"]!.GetValue<string>());
        Assert.Equal(error.Details["traceId"], info["metadata"]!["traceId"]!.GetValue<string>());
    }

    [Fact]
    public void Generated_error_carries_reason_and_trace_id_on_grpc()
    {
        var error = Generated();
        var status = GrpcErrorMapper.ToRpcException(error).GetRpcStatus()!;

        var info = status.GetDetail<ErrorInfo>();
        Assert.Equal("INTERNAL", info.Reason);
        Assert.Equal(error.Details["traceId"], info.Metadata["traceId"]);
    }

    [Fact]
    public void Handler_internal_error_data_stays_off_the_wire()
    {
        var error = FromHandler();

        Assert.Null(JsonNode.Parse(RestErrorWriter.ToJson(error))!["error"]!["details"]);
        Assert.Empty(GrpcErrorMapper.ToRpcException(error).GetRpcStatus()!.Details);
        Assert.Null(TransportErrors.ReasonFor(error));
    }

    [Fact]
    public void Generated_error_data_is_only_the_trace_id()
        => Assert.Single(Generated().Details);
}
