using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client.Tests;

public sealed class ErrorTypeTests
{
    [Fact]
    public void Auth_exception_from_http_401_is_unauthenticated()
    {
        var ex = new A2AAuthException(401, "x");
        Assert.Equal(A2AAuthFailureKind.Unauthenticated, ex.Kind);
        Assert.Equal(401, ex.HttpStatus);
        Assert.Null(ex.GrpcStatus);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(407)]
    public void Auth_exception_from_other_http_status_is_forbidden(int status)
        => Assert.Equal(A2AAuthFailureKind.Forbidden, new A2AAuthException(status, "x").Kind);

    [Fact]
    public void Auth_exception_for_grpc_has_no_http_status()
    {
        var ex = new A2AAuthException(A2AAuthFailureKind.Forbidden, "x", httpStatus: null, grpcStatus: "PERMISSION_DENIED");
        Assert.Null(ex.HttpStatus);
        Assert.Equal("PERMISSION_DENIED", ex.GrpcStatus);
        Assert.Equal(A2AAuthFailureKind.Forbidden, ex.Kind);
    }

    [Fact]
    public void Auth_exception_carries_the_oauth_error_fields()
    {
        var ex = new A2AAuthException(A2AAuthFailureKind.Unauthenticated, "x", httpStatus: 401, error: "invalid_token", errorDescription: "expired");
        Assert.Equal("invalid_token", ex.Error);
        Assert.Equal("expired", ex.ErrorDescription);
    }

    [Theory]
    [InlineData(503, true)]
    [InlineData(500, true)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(404, false)]
    [InlineData(400, false)]
    public void Transport_exception_marks_transient_statuses(int status, bool transient)
    {
        var ex = A2ATransportException.FromHttpStatus(status, "billing", "SendMessage");
        Assert.Equal(transient, ex.IsTransient);
        Assert.Equal(status, ex.HttpStatus);
        Assert.Null(ex.GrpcStatus);
        Assert.IsAssignableFrom<A2AException>(ex);
        Assert.Equal(A2AErrorCode.InternalError, ex.Code);
        Assert.Equal($"SendMessage to agent 'billing' failed with HTTP {status}.", ex.Message);
    }

    [Fact]
    public void Timeout_exception_is_an_A2AException_and_names_the_option()
    {
        var ex = new A2ATimeoutException("SendMessage (blocking)", TimeSpan.FromMinutes(10), "A2AClientOptions.Timeouts.BlockingSend", "use SendAndWaitAsync");
        Assert.IsAssignableFrom<A2AException>(ex);
        Assert.Equal(A2AErrorCode.InternalError, ex.Code);
        Assert.Equal("SendMessage (blocking) timed out after 10 min; raise A2AClientOptions.Timeouts.BlockingSend or use SendAndWaitAsync.", ex.Message);
    }

    [Fact]
    public void Timeout_exception_formats_seconds_and_works_without_an_alternative()
    {
        var ex = new A2ATimeoutException("GetTask", TimeSpan.FromSeconds(30), "A2AClientOptions.Timeouts.Unary");
        Assert.Equal("GetTask timed out after 30 s; raise A2AClientOptions.Timeouts.Unary.", ex.Message);
    }

    [Fact]
    public void Stream_interrupted_exception_has_the_spec_message()
    {
        var ex = new A2AStreamInterruptedException();
        Assert.IsAssignableFrom<A2AException>(ex);
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
        Assert.Equal("the stream ended before the agent returned a task; the message may or may not have been accepted.", ex.Message);
    }
}
