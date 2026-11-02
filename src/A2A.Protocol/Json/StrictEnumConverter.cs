using System.Text.Json.Serialization;

namespace A2A.Protocol;

/// <summary>String enum converter that honors <c>JsonStringEnumMemberName</c> and rejects integers.</summary>
public sealed class StrictEnumConverter<T> : JsonStringEnumConverter<T> where T : struct, Enum
{
    public StrictEnumConverter() : base(namingPolicy: null, allowIntegerValues: false) { }
}
