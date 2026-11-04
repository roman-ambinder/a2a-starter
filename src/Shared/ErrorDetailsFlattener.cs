using System.Text.Json;

namespace Ambinder.A2A.Shared;

/// <summary>
/// Flattens <c>A2AException.Data</c> into the string map that ErrorInfo <c>metadata</c> and <c>A2AException.Details</c> use.
/// Linked as one source file into the projects that need it, compiled <c>internal</c> in each.
/// </summary>
// Warning: never add InternalsVisibleTo from Ambinder.A2A.Protocol to a project that also sees Ambinder.A2A.Server's copy of this type (CS0433).
internal static class ErrorDetailsFlattener
{
    /// <summary>
    /// The members of a JSON object: strings as they are, arrays joined with <c>,</c>, anything else as raw JSON text.
    /// Not an object, or null: nothing.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> Flatten(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } obj) yield break;
        foreach (var p in obj.EnumerateObject())
            yield return KeyValuePair.Create(p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : FlattenValue(p.Value));
    }

    /// <summary>The same pairs as a dictionary. A repeated name keeps the last value.</summary>
    public static Dictionary<string, string> ToMap(JsonElement? data)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in Flatten(data)) map[name] = value;
        return map;
    }

    private static string FlattenValue(JsonElement value)
        => value.ValueKind == JsonValueKind.Array
            ? string.Join(",", value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()))
            : value.GetRawText();
}
