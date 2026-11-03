using System.Text.Json;

namespace Ambinder.A2A.Protocol;

/// <summary>An exception that carries an A2A error code.</summary>
public class A2AException(A2AErrorCode code, string message, JsonElement? data = null) : Exception(message)
{
    /// <summary>The A2A error code.</summary>
    public A2AErrorCode Code { get; } = code;

    /// <summary>Optional extra data for the error. Hides <see cref="Exception.Data"/> on purpose: the brief fixes this name.</summary>
    public new JsonElement? Data { get; } = data;
}
