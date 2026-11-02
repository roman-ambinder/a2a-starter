namespace A2A.Protocol.Tests.Errors;

public class ErrorCatalogTests
{
    [Theory]
    [InlineData(A2AErrorCode.ParseError, -32700, 400, "InvalidArgument", null)]
    [InlineData(A2AErrorCode.InvalidRequest, -32600, 400, "InvalidArgument", null)]
    [InlineData(A2AErrorCode.MethodNotFound, -32601, 404, "Unimplemented", null)]
    [InlineData(A2AErrorCode.InvalidParams, -32602, 400, "InvalidArgument", null)]
    [InlineData(A2AErrorCode.InternalError, -32603, 500, "Internal", null)]
    [InlineData(A2AErrorCode.TaskNotFound, -32001, 404, "NotFound", "TASK_NOT_FOUND")]
    [InlineData(A2AErrorCode.TaskNotCancelable, -32002, 400, "FailedPrecondition", "TASK_NOT_CANCELABLE")]
    [InlineData(A2AErrorCode.PushNotificationNotSupported, -32003, 400, "FailedPrecondition", "PUSH_NOTIFICATION_NOT_SUPPORTED")]
    [InlineData(A2AErrorCode.UnsupportedOperation, -32004, 400, "FailedPrecondition", "UNSUPPORTED_OPERATION")]
    [InlineData(A2AErrorCode.ContentTypeNotSupported, -32005, 400, "InvalidArgument", "CONTENT_TYPE_NOT_SUPPORTED")]
    [InlineData(A2AErrorCode.InvalidAgentResponse, -32006, 500, "Internal", "INVALID_AGENT_RESPONSE")]
    [InlineData(A2AErrorCode.ExtendedAgentCardNotConfigured, -32007, 400, "FailedPrecondition", "EXTENDED_AGENT_CARD_NOT_CONFIGURED")]
    [InlineData(A2AErrorCode.ExtensionSupportRequired, -32008, 400, "FailedPrecondition", "EXTENSION_SUPPORT_REQUIRED")]
    [InlineData(A2AErrorCode.VersionNotSupported, -32009, 400, "FailedPrecondition", "VERSION_NOT_SUPPORTED")]
    public void Catalog_row_matches_wire_reference(A2AErrorCode code, int jsonRpcCode, int httpStatus, string grpcStatus, string? reason)
    {
        var info = A2AError.Get(code);

        Assert.Equal(code, info.Code);
        Assert.Equal(jsonRpcCode, (int)info.Code);
        Assert.Equal(httpStatus, info.HttpStatus);
        Assert.Equal(grpcStatus, info.GrpcStatus);
        Assert.Equal(reason, info.Reason);
    }

    [Theory]
    [InlineData("TASK_NOT_FOUND", A2AErrorCode.TaskNotFound)]
    [InlineData("TASK_NOT_CANCELABLE", A2AErrorCode.TaskNotCancelable)]
    [InlineData("PUSH_NOTIFICATION_NOT_SUPPORTED", A2AErrorCode.PushNotificationNotSupported)]
    [InlineData("UNSUPPORTED_OPERATION", A2AErrorCode.UnsupportedOperation)]
    [InlineData("CONTENT_TYPE_NOT_SUPPORTED", A2AErrorCode.ContentTypeNotSupported)]
    [InlineData("INVALID_AGENT_RESPONSE", A2AErrorCode.InvalidAgentResponse)]
    [InlineData("EXTENDED_AGENT_CARD_NOT_CONFIGURED", A2AErrorCode.ExtendedAgentCardNotConfigured)]
    [InlineData("EXTENSION_SUPPORT_REQUIRED", A2AErrorCode.ExtensionSupportRequired)]
    [InlineData("VERSION_NOT_SUPPORTED", A2AErrorCode.VersionNotSupported)]
    public void FromReason_maps_each_reason_to_its_code(string reason, A2AErrorCode expected)
        => Assert.Equal(expected, A2AError.FromReason(reason));

    [Fact]
    public void FromReason_returns_null_for_unknown_reason()
        => Assert.Null(A2AError.FromReason("NOT_A_REAL_REASON"));

    [Fact]
    public void FromReason_is_case_sensitive()
        => Assert.Null(A2AError.FromReason("task_not_found"));

    [Fact]
    public void Domain_is_a2a_protocol_org()
        => Assert.Equal("a2a-protocol.org", A2AError.Domain);

    [Fact]
    public void Catalog_has_fourteen_codes()
        => Assert.Equal(14, Enum.GetValues<A2AErrorCode>().Length);

    [Fact]
    public void Exception_carries_code_message_and_data()
    {
        var data = System.Text.Json.JsonSerializer.SerializeToElement(new { taskId = "t1" });
        var ex = new A2AException(A2AErrorCode.TaskNotFound, "missing", data);

        Assert.Equal(A2AErrorCode.TaskNotFound, ex.Code);
        Assert.Equal("missing", ex.Message);
        Assert.Equal("t1", ex.Data!.Value.GetProperty("taskId").GetString());
    }

    [Fact]
    public void Exception_data_defaults_to_null()
        => Assert.Null(new A2AException(A2AErrorCode.InternalError, "boom").Data);

    [Fact]
    public void Auth_exception_carries_http_status_and_message()
    {
        var ex = new A2AAuthException(401, "no token");

        Assert.Equal(401, ex.HttpStatus);
        Assert.Equal("no token", ex.Message);
    }
}
