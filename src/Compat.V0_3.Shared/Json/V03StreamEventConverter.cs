using System.Text.Json;
using System.Text.Json.Serialization;
using Ambinder.A2A.Compat.V0_3.Shared.Models;

namespace Ambinder.A2A.Compat.V0_3.Shared.Json;

/// <summary>One 0.3 stream event: task, message, status-update or artifact-update (a2a.json <c>SendStreamingMessageSuccessResponse.result</c>).</summary>
internal sealed class V03StreamEventConverter : JsonConverter<IV03StreamEvent>
{
    public override IV03StreamEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => V03UnionReader.Read(ref reader, options, allowEvents: true);

    public override void Write(Utf8JsonWriter writer, IV03StreamEvent value, JsonSerializerOptions options)
        => V03UnionReader.Write(writer, value, options);
}
