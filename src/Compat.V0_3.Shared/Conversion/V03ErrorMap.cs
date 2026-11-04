using Ambinder.A2A.Compat.V0_3.Shared.Models;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Compat.V0_3.Shared.Conversion;

/// <summary>
/// Maps error codes between 1.0 and 0.3. The full table is in docs/spec-decisions.md, "A2A 0.3 compatibility: error codes".
/// The 12 codes of a2a.json <c>A2AError</c> that 1.0 also has keep their number. The two 1.0-only codes (<c>ExtensionSupportRequired</c>,
/// <c>VersionNotSupported</c>) map to <c>InvalidRequest</c> (plan writer's call). The JSON-RPC <c>data</c> member is
/// whatever JSON value the exception carries (a2a.json <c>JSONRPCError.data</c> is any value), never the 1.0 <c>ErrorInfo</c> array.
/// </summary>
internal static class V03ErrorMap
{
    /// <summary>The 0.3 code for a 1.0 error code.</summary>
    public static int ToV03Code(A2AErrorCode code) => code switch
    {
        A2AErrorCode.ExtensionSupportRequired or A2AErrorCode.VersionNotSupported => (int)A2AErrorCode.InvalidRequest,
        _ when IsSharedCode((int)code) => (int)code,
        _ => (int)A2AErrorCode.InternalError,
    };

    /// <summary>
    /// The 1.0 error code for a 0.3 code, or <see langword="null"/> when 0.3 does not define the code
    /// (for example another number in the -32099 to -32000 server range).
    /// </summary>
    public static A2AErrorCode? FromV03Code(int code) => IsSharedCode(code) ? (A2AErrorCode)code : null;

    /// <summary>Builds the 0.3 <c>error</c> object for an exception: mapped code, the same message, and <see cref="A2AException.Data"/> as <c>data</c>.</summary>
    public static V03JsonRpcError ToV03(A2AException exception) => new()
    {
        Code = ToV03Code(exception.Code),
        Message = exception.Message,
        Data = exception.Data,
    };

    /// <summary>
    /// Builds the exception for a 0.3 <c>error</c> object. A code that 0.3 does not define gives <see cref="A2AErrorCode.InternalError"/>
    /// (the number is lost; implementer default, see docs/spec-decisions.md). <c>data</c> is kept as it came.
    /// </summary>
    public static A2AException FromV03(V03JsonRpcError error)
        => new(FromV03Code(error.Code) ?? A2AErrorCode.InternalError, error.Message, error.Data);

    // -32700, -32600..-32603 and -32001..-32007 are the numbers a2a.json defines for A2AError (inventory.json, errorCodes).
    private static bool IsSharedCode(int code) => code is -32700 or (>= -32603 and <= -32600) or (>= -32007 and <= -32001);
}
