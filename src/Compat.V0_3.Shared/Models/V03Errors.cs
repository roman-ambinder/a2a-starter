using System.Text.Json;

namespace Ambinder.A2A.Compat.V0_3.Shared.Models;

/// <summary>a2a.json <c>JSONRPCError</c>: the <c>error</c> member of a JSON-RPC error response.</summary>
internal sealed record V03JsonRpcError
{
    public int Code { get; init; }
    public string Message { get; init; } = "";
    /// <summary>Any JSON value (a2a.json: "A primitive or structured value ... may be omitted").</summary>
    public JsonElement? Data { get; init; }
}

/// <summary>
/// RFC 9457 problem details for 0.3 REST errors. NOTE: the vendored v0.3.0 files define no REST error body
/// (docs/spec-decisions.md, "REST error format"); these members are the RFC 9457 standard ones, which the plan
/// asks for. The REST tasks (11, 16) may add members.
/// </summary>
internal sealed record V03ProblemDetails
{
    public string? Type { get; init; }
    public string? Title { get; init; }
    public int? Status { get; init; }
    public string? Detail { get; init; }
    public string? Instance { get; init; }
}
