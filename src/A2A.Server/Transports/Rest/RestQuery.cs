using System.Globalization;
using System.Text.Json.Nodes;
using A2A.Protocol;
using Microsoft.AspNetCore.Http;

namespace A2A.Server.Transports.Rest;

/// <summary>Turns query parameters into a typed request. Wrong types become InvalidParams.</summary>
internal static class RestQuery
{
    internal enum Kind { String, Int, Bool, Timestamp, Enum }

    /// <summary>
    /// Builds a JSON object from the path values plus the listed query parameters (camelCase names), then deserializes it.
    /// Query parameters not in <paramref name="allowed"/> (including <c>A2A-Version</c>) are ignored. Path values win.
    /// </summary>
    public static T Bind<T>(IQueryCollection query, IReadOnlyDictionary<string, Kind> allowed, params (string Name, string Value)[] pathValues)
    {
        var obj = new JsonObject();
        foreach (var (name, kind) in allowed)
        {
            if (!query.TryGetValue(name, out var values) || values.Count == 0) continue;
            var raw = values[0] ?? "";
            obj[name] = kind switch
            {
                Kind.Int => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                    ? JsonValue.Create(i) : throw Bad(name, "an integer"),
                Kind.Bool => raw switch { "true" => JsonValue.Create(true), "false" => JsonValue.Create(false), _ => throw Bad(name, "true or false") },
                Kind.Timestamp => DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)
                    ? JsonValue.Create(raw) : throw Bad(name, "an ISO 8601 timestamp"),
                _ => JsonValue.Create(raw), // String; Enum: an unknown name reads as UNSPECIFIED (ProtoEnumConverter)
            };
        }
        foreach (var (name, value) in pathValues) obj[name] = value;
        return (T)TransportErrors.Deserialize(JsonSerializerElement(obj), typeof(T));
    }

    private static System.Text.Json.JsonElement JsonSerializerElement(JsonObject obj)
        => System.Text.Json.JsonSerializer.SerializeToElement(obj);

    private static A2AException Bad(string name, string expected)
        => new(A2AErrorCode.InvalidParams, $"Invalid params: query parameter '{name}' must be {expected}.");
}
