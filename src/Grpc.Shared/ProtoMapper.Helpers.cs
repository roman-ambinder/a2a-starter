using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Proto = Lf.A2a.V1;

namespace Ambinder.A2A.Protocol.Grpc;

/// <summary>Maps Ambinder.A2A.Protocol models to and from the generated gRPC types (Lf.A2a.V1).</summary>
#if A2A_GRPC_INTERNAL
internal
#else
public
#endif
static partial class ProtoMapper
{
    private static string S(string? value) => value ?? "";

    private static string? N(string value) => value.Length == 0 ? null : value;

    private static List<T> L<T>(IEnumerable<T> items) => [.. items];

    /// <summary>Maps by member name. A value the target does not define (a newer peer's number) becomes the zero value (UNSPECIFIED), as in JSON.</summary>
    private static TTo EnumByName<TFrom, TTo>(TFrom value) where TFrom : struct, System.Enum where TTo : struct, System.Enum
        => System.Enum.IsDefined(value) && System.Enum.TryParse<TTo>(value.ToString(), ignoreCase: false, out var mapped) && System.Enum.IsDefined(mapped)
            ? mapped
            : default;

    internal static Proto.TaskState ToProto(TaskState value) => EnumByName<TaskState, Proto.TaskState>(value);

    internal static TaskState FromProto(Proto.TaskState value) => EnumByName<Proto.TaskState, TaskState>(value);

    internal static Proto.Role ToProto(Role value) => EnumByName<Role, Proto.Role>(value);

    internal static Role FromProto(Proto.Role value) => EnumByName<Proto.Role, Role>(value);

    internal static Struct? ToStruct(JsonElement? value)
        => value is { } element ? Struct.Parser.ParseJson(element.GetRawText()) : null;

    internal static JsonElement? FromStruct(Struct? value)
        => value is null ? null : ParseElement(JsonFormatter.Default.Format(value));

    internal static Value? ToValue(JsonElement? value)
        => value is { } element ? Value.Parser.ParseJson(element.GetRawText()) : null;

    internal static JsonElement? FromValue(Value? value)
        => value is null ? null : ParseElement(JsonFormatter.Default.Format(value));

    private static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    internal static Timestamp? ToTimestamp(DateTimeOffset? value)
        => value is { } v ? Timestamp.FromDateTimeOffset(v) : null;

    internal static DateTimeOffset? FromTimestamp(Timestamp? value)
        => value?.ToDateTimeOffset();

    private static Dictionary<string, string> ToDict(IReadOnlyDictionary<string, string> value)
        => new(value);
}
