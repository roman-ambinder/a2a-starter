using System.Collections.ObjectModel;
using System.Text.Json;
using Ambinder.A2A.Shared;

namespace Ambinder.A2A.Protocol;

/// <summary>An exception that carries an A2A error code.</summary>
public class A2AException(A2AErrorCode code, string message, JsonElement? data = null) : Exception(message)
{
    /// <summary>The A2A error code.</summary>
    public A2AErrorCode Code { get; } = code;

    /// <summary>Optional extra data for the error. Hides <see cref="Exception.Data"/> on purpose: the brief fixes this name.</summary>
    public new JsonElement? Data { get; } = data;

    /// <summary>
    /// <see cref="Data"/> as a read-only string map, the same shape as the ErrorInfo <c>metadata</c> on the wire. Computed once.
    /// Strings stay as they are, other values become raw JSON text. Empty when <see cref="Data"/> is null or not a JSON object.
    /// </summary>
    public IReadOnlyDictionary<string, string> Details { get; } =
        new ReadOnlyDictionary<string, string>(ErrorDetailsFlattener.ToMap(data));
}
