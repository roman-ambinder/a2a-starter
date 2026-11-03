using System.Text.Json;
using Ambinder.A2A.Protocol.Grpc;
using Google.Protobuf;

namespace Ambinder.A2A.Protocol.Tests.Grpc;

public class ProtoRoundTripTests
{
    public static TheoryData<object> AllSamples => Samples.All();

    private static string Ours(object value) => JsonSerializer.Serialize(value, value.GetType(), A2AJson.Options);

    [Theory, MemberData(nameof(AllSamples))]
    public void Our_json_parses_with_official_proto_parser(object sample)
    {
        var proto = ProtoJson.Parse(sample.GetType(), Ours(sample));
        Assert.NotNull(proto);
    }

    [Theory, MemberData(nameof(AllSamples))]
    public void Proto_round_trip_preserves_values(object sample)
    {
        var back = ProtoMapper.FromProtoObject(ProtoMapper.ToProtoObject(sample));
        Assert.Equal(Ours(sample), Ours(back));
    }

    [Theory, MemberData(nameof(AllSamples))]
    public void Official_formatter_output_deserializes_with_our_options(object sample)
    {
        var protoJson = JsonFormatter.Default.Format(ProtoMapper.ToProtoObject(sample));
        var ours = JsonSerializer.Deserialize(protoJson, sample.GetType(), A2AJson.Options)!;
        Assert.Equal(Ours(sample), Ours(ours));
    }

    [Theory, MemberData(nameof(AllSamples))]
    public void Our_json_equals_official_formatter_output_semantically(object sample)
    {
        var protoJson = JsonFormatter.Default.Format(ProtoMapper.ToProtoObject(sample));
        var parsed = ProtoJson.Parse(sample.GetType(), Ours(sample));
        Assert.Equal(protoJson, JsonFormatter.Default.Format(parsed));
    }
}
