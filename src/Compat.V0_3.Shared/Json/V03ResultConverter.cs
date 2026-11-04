using System.Text.Json;
using System.Text.Json.Serialization;
using Ambinder.A2A.Compat.V0_3.Shared.Models;

namespace Ambinder.A2A.Compat.V0_3.Shared.Json;

/// <summary>The result of 0.3 <c>message/send</c>: a task or a message (a2a.json <c>SendMessageSuccessResponse.result</c>).</summary>
internal sealed class V03ResultConverter : JsonConverter<IV03Result>
{
    public override IV03Result Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => (IV03Result)V03UnionReader.Read(ref reader, options, allowEvents: false);

    public override void Write(Utf8JsonWriter writer, IV03Result value, JsonSerializerOptions options)
        => V03UnionReader.Write(writer, (IV03StreamEvent)value, options);
}
