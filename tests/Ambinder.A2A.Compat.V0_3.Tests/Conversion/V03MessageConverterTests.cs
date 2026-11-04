using System.Text;
using System.Text.Json;
using Ambinder.A2A.Compat.V0_3.Shared.Conversion;
using Ambinder.A2A.Compat.V0_3.Shared.Models;
using Ambinder.A2A.Protocol;
using static Ambinder.A2A.Compat.V0_3.Tests.Conversion.ConversionFixtures;

namespace Ambinder.A2A.Compat.V0_3.Tests.Conversion;

public class V03MessageConverterTests
{
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("hello bytes");
    private static readonly string Base64 = Convert.ToBase64String(Bytes);

    [Fact]
    public void Text_part_maps_both_ways_with_metadata()
    {
        var v03 = new V03TextPart("hi") { Metadata = Obj("""{"a":1}""") };
        Part v1 = V03MessageConverter.ToV1(v03);
        Assert.Equal("hi", v1.Text);
        Assert.Equal("""{"a":1}""", v1.Metadata!.Value.GetRawText());
        Assert.True(v1.IsValid);

        var back = Assert.IsType<V03TextPart>(V03MessageConverter.ToV03(v1));
        Assert.Equal("hi", back.Text);
        Assert.Equal(1, back.Metadata!["a"].GetInt32());
    }

    [Fact]
    public void File_with_bytes_maps_to_raw_with_mime_type_and_name()
    {
        var v03 = new V03FilePart(new V03FileContent { Bytes = Base64, MimeType = "image/png", Name = "a.png" });
        Part v1 = V03MessageConverter.ToV1(v03);
        Assert.Equal(Bytes, v1.Raw);
        Assert.Null(v1.Url);
        Assert.Equal("image/png", v1.MediaType);
        Assert.Equal("a.png", v1.Filename);

        var back = Assert.IsType<V03FilePart>(V03MessageConverter.ToV03(v1));
        Assert.Equal(Base64, back.File.Bytes);
        Assert.Null(back.File.Uri);
        Assert.Equal("image/png", back.File.MimeType);
        Assert.Equal("a.png", back.File.Name);
    }

    [Fact]
    public void File_with_uri_maps_to_url_with_mime_type_and_name()
    {
        var v03 = new V03FilePart(new V03FileContent { Uri = "https://x.example/a.png", MimeType = "image/png", Name = "a.png" });
        Part v1 = V03MessageConverter.ToV1(v03);
        Assert.Equal("https://x.example/a.png", v1.Url);
        Assert.Null(v1.Raw);
        Assert.Equal("image/png", v1.MediaType);
        Assert.Equal("a.png", v1.Filename);

        var back = Assert.IsType<V03FilePart>(V03MessageConverter.ToV03(v1));
        Assert.Equal("https://x.example/a.png", back.File.Uri);
        Assert.Null(back.File.Bytes);
        Assert.Equal("a.png", back.File.Name);
    }

    [Fact]
    public void File_without_name_and_mime_type_keeps_them_null()
    {
        Part v1 = V03MessageConverter.ToV1(new V03FilePart(new V03FileContent { Bytes = Base64 }));
        Assert.Null(v1.MediaType);
        Assert.Null(v1.Filename);
        var back = Assert.IsType<V03FilePart>(V03MessageConverter.ToV03(v1));
        Assert.Null(back.File.MimeType);
        Assert.Null(back.File.Name);
    }

    [Fact]
    public void Empty_raw_file_stays_an_empty_file_with_bytes()
    {
        var back = Assert.IsType<V03FilePart>(V03MessageConverter.ToV03(Part.FromRaw([], "application/octet-stream")));
        Assert.Equal("", back.File.Bytes);
        Assert.Null(back.File.Uri);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void File_with_both_or_neither_of_bytes_and_uri_is_invalid_params(bool bytes, bool uri)
    {
        var file = new V03FileContent { Bytes = bytes ? Base64 : null, Uri = uri ? "https://x.example/a" : null };
        var ex = Assert.Throws<A2AException>(() => V03MessageConverter.ToV1(new V03FilePart(file)));
        Assert.Equal(A2AErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void File_bytes_that_are_not_base64_are_invalid_params()
    {
        var ex = Assert.Throws<A2AException>(() => V03MessageConverter.ToV1(new V03FilePart(new V03FileContent { Bytes = "***" })));
        Assert.Equal(A2AErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Data_part_maps_both_ways()
    {
        var v03 = new V03DataPart(Obj("""{"city":"Oslo","n":[1,2]}""")) { Metadata = Obj("""{"m":true}""") };
        Part v1 = V03MessageConverter.ToV1(v03);
        Assert.Equal("""{"city":"Oslo","n":[1,2]}""", v1.Data!.Value.GetRawText());
        Assert.True(v1.Metadata!.Value.GetProperty("m").GetBoolean());

        var back = Assert.IsType<V03DataPart>(V03MessageConverter.ToV03(v1));
        Assert.Equal("Oslo", back.Data["city"].GetString());
        Assert.True(back.Metadata!["m"].GetBoolean());
    }

    [Fact]
    public void Empty_data_object_is_kept()
    {
        Part v1 = V03MessageConverter.ToV1(new V03DataPart(Obj("{}")));
        Assert.Equal(JsonValueKind.Object, v1.Data!.Value.ValueKind);
        Assert.Empty(Assert.IsType<V03DataPart>(V03MessageConverter.ToV03(v1)).Data);
    }

    [Fact]
    public void Data_that_is_not_an_object_is_wrapped_as_value_implementer_default()
    {
        var back = Assert.IsType<V03DataPart>(V03MessageConverter.ToV03(Part.FromData(ParseJson("[1,2]"))));
        Assert.Equal("[1,2]", back.Data["value"].GetRawText());
    }

    [Fact]
    public void Media_type_and_filename_of_a_text_or_data_part_have_no_0_3_home_and_are_dropped()
    {
        var text = Assert.IsType<V03TextPart>(V03MessageConverter.ToV03(new Part { Text = "x", MediaType = "text/markdown", Filename = "x.md" }));
        Assert.Equal("x", text.Text);
        Assert.IsType<V03DataPart>(V03MessageConverter.ToV03(new Part { Data = ParseJson("{}"), MediaType = "application/json" }));
    }

    [Fact]
    public void Part_metadata_that_is_not_an_object_is_dropped()
    {
        var back = V03MessageConverter.ToV03(new Part { Text = "x", Metadata = ParseJson("[1]") });
        Assert.Null(back.Metadata);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Part_with_zero_or_two_contents_cannot_be_written_as_0_3(int contents)
    {
        var part = contents == 0 ? new Part() : new Part { Text = "a", Url = "https://x.example/a" };
        var ex = Assert.Throws<A2AException>(() => V03MessageConverter.ToV03(part));
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
    }

    [Fact]
    public void Message_copies_every_field_both_ways()
    {
        var v03 = new V03Message
        {
            Role = "user",
            MessageId = "m-1",
            ContextId = "c-1",
            TaskId = "t-1",
            ReferenceTaskIds = ["r-1", "r-2"],
            Extensions = ["https://example.com/ext/v1"],
            Metadata = Obj("""{"k":"v"}"""),
            Parts = [new V03TextPart("hi")],
        };

        Message v1 = V03MessageConverter.ToV1(v03);
        Assert.Equal("m-1", v1.MessageId);
        Assert.Equal("c-1", v1.ContextId);
        Assert.Equal("t-1", v1.TaskId);
        Assert.Equal(Role.User, v1.Role);
        Assert.Equal(["r-1", "r-2"], v1.ReferenceTaskIds);
        Assert.Equal(["https://example.com/ext/v1"], v1.Extensions);
        Assert.Equal("v", v1.Metadata!.Value.GetProperty("k").GetString());
        Assert.Equal("hi", Assert.Single(v1.Parts).Text);

        V03Message back = V03MessageConverter.ToV03(v1);
        Assert.Equal("message", back.Kind);
        Assert.Equal("user", back.Role);
        Assert.Equal("m-1", back.MessageId);
        Assert.Equal("c-1", back.ContextId);
        Assert.Equal("t-1", back.TaskId);
        Assert.Equal(["r-1", "r-2"], back.ReferenceTaskIds);
        Assert.Equal(["https://example.com/ext/v1"], back.Extensions);
        Assert.Equal("v", back.Metadata!["k"].GetString());
    }

    [Fact]
    public void Agent_role_and_missing_optional_members()
    {
        Message v1 = V03MessageConverter.ToV1(new V03Message { Role = "agent", MessageId = "m", Parts = [new V03TextPart("x")] });
        Assert.Equal(Role.Agent, v1.Role);
        Assert.Null(v1.ContextId);
        Assert.Null(v1.TaskId);
        Assert.Empty(v1.Extensions);
        Assert.Empty(v1.ReferenceTaskIds);
        Assert.Null(v1.Metadata);

        V03Message back = V03MessageConverter.ToV03(v1);
        Assert.Equal("agent", back.Role);
        Assert.Null(back.Extensions);
        Assert.Null(back.ReferenceTaskIds);
        Assert.Null(back.Metadata);
    }

    [Fact]
    public void Message_without_role_reads_as_unspecified_and_writes_no_role()
    {
        Message v1 = V03MessageConverter.ToV1(new V03Message { MessageId = "m", Parts = [new V03TextPart("x")] });
        Assert.Equal(Role.Unspecified, v1.Role);
        Assert.Null(V03MessageConverter.ToV03(v1).Role);
    }

    [Fact]
    public void Empty_metadata_object_stays_an_empty_object()
    {
        Message v1 = V03MessageConverter.ToV1(new V03Message { MessageId = "m", Metadata = Obj("{}"), Parts = [new V03TextPart("x")] });
        Assert.Equal("{}", v1.Metadata!.Value.GetRawText());
        Assert.Empty(V03MessageConverter.ToV03(v1).Metadata!);
    }

    [Theory]
    [MemberData(nameof(MessageResults))]
    public void Fixture_message_round_trips_0_3_to_1_0_to_0_3(string rel)
    {
        var original = Assert.IsType<V03Message>(Result(rel));
        V03Message back = V03MessageConverter.ToV03(V03MessageConverter.ToV1(original));
        Assert.True(
            System.Text.Json.Nodes.JsonNode.DeepEquals(Normalized(original, Ctx.V03Message), Normalized(back, Ctx.V03Message)),
            rel);
    }

    [Theory]
    [MemberData(nameof(MessageResults))]
    public void Fixture_message_round_trips_1_0_to_0_3_to_1_0(string rel)
    {
        Message v1 = V03MessageConverter.ToV1(Assert.IsType<V03Message>(Result(rel)));
        Message again = V03MessageConverter.ToV1(V03MessageConverter.ToV03(v1));
        Assert.Equal(V1(v1, A2AJson.Message), V1(again, A2AJson.Message));
    }

    public static TheoryData<string> MessageResults() => Data("responses", f => Result(f) is V03Message);

    [Fact]
    public void Fixture_file_part_with_uri_keeps_name_media_type_and_part_metadata()
    {
        var message = Assert.IsType<V03Message>(Result("responses/message-send.message-file-part-uri.json"));
        Message v1 = V03MessageConverter.ToV1(message);
        Part part = Assert.Single(v1.Parts);
        Assert.Equal("https://files.example.com/photo.png", part.Url);
        Assert.Equal("photo.png", part.Filename);
        Assert.Equal("image/png", part.MediaType);
        Assert.Equal("unit-test", part.Metadata!.Value.GetProperty("source").GetString());
        Assert.Equal(["9d7e5c3a-0000-4000-8000-000000000001"], v1.ReferenceTaskIds);
        Assert.Equal(["https://example.com/ext/demo/v1"], v1.Extensions);
        Assert.Equal("edge case", v1.Metadata!.Value.GetProperty("note").GetString());
    }

    [Fact]
    public void Members_missing_on_the_wire_are_null_at_run_time_and_convert_without_throwing()
    {
        // The 0.3 models do not apply their initializers to a missing member, so a spec example without messageId or parts reads as null there.
        var empty = System.Text.Json.JsonSerializer.Deserialize("{}", Ctx.V03Message)!;
        Message v1 = V03MessageConverter.ToV1(empty);
        Assert.Equal("", v1.MessageId);
        Assert.Empty(v1.Parts);
    }
}
