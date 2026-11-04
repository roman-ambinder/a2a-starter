using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ambinder.A2A.Protocol;

/// <summary>
/// ProtoJSON enum converter. Writes the <c>JsonStringEnumMemberName</c> name, never a number. Reads a known name or a known
/// integer (ProtoJSON parsers accept both). An unknown name or number reads as the zero value (<c>*_UNSPECIFIED</c>) instead of
/// failing the whole payload, so a newer peer's enum values do not break older readers (spec 5: ignore what you do not know).
/// Servers reject <c>Unspecified</c> where a value is required. The name tables are built once per enum type.
/// </summary>
/// <remarks>
/// This type is public only because the public enums <see cref="TaskState"/> and <see cref="Role"/> name it in
/// <c>[JsonConverter]</c>. The System.Text.Json source generator needs an accessible converter, so an app's own
/// <c>JsonSerializerContext</c> that lists A2A models compiles only if this class is public. Apps do not use it directly.
/// </remarks>
public sealed class ProtoEnumConverter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>
    : JsonConverter<T> where T : struct, Enum
{
    private static readonly Dictionary<string, T> ByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<T, string> ToName = [];
    private static readonly Dictionary<int, T> ByNumber = [];
    private static readonly KeyValuePair<string, object?> EnumTag = new("a2a.enum", typeof(T).Name);

    static ProtoEnumConverter()
    {
        foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var value = (T)field.GetValue(null)!;
            var name = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? field.Name;
            ByName[name] = value;
            ToName[value] = name;
            ByNumber[Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)] = value;
        }
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                if (ByName.TryGetValue(reader.GetString()!, out var named)) return named;
                break;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var number) && ByNumber.TryGetValue(number, out var numbered)) return numbered;
                break;
            default:
                throw new JsonException($"Expected a string for {typeof(T).Name}.");
        }

        ProtocolInstruments.UnknownEnumValues.Add(1, EnumTag);
        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToName.TryGetValue(value, out var name) ? name : ToName[default]);
}

/// <summary>Metrics of the protocol library.</summary>
internal static class ProtocolInstruments
{
    private static readonly Meter Meter = new("Ambinder.A2A.Protocol");

    /// <summary>Enum values read from the wire that this version does not know (read as <c>*_UNSPECIFIED</c>). Tag <c>a2a.enum</c>.</summary>
    public static readonly Counter<long> UnknownEnumValues =
        Meter.CreateCounter<long>("a2a.protocol.unknown_enum_values", description: "Unknown enum values read as UNSPECIFIED.");
}
