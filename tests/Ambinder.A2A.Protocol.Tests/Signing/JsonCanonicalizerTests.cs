using System.Buffers;
using System.Text;
using System.Text.Json;
using Ambinder.A2A.Protocol.Signing;

namespace Ambinder.A2A.Protocol.Tests.Signing;

public sealed class JsonCanonicalizerTests
{
    private static string Jcs(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Encoding.UTF8.GetString(JsonCanonicalizer.Canonicalize(doc.RootElement));
    }

    [Theory]
    [InlineData("{ \"b\" : 1 , \"a\" : [ true , false , null ] }", "{\"a\":[true,false,null],\"b\":1}")]
    [InlineData("[]", "[]")]
    [InlineData("{}", "{}")]
    [InlineData("{\"a\":{\"d\":1,\"c\":{}}}", "{\"a\":{\"c\":{},\"d\":1}}")]
    public void Removes_whitespace_and_sorts_members(string input, string expected) => Assert.Equal(expected, Jcs(input));

    [Fact]
    public void Sorts_by_utf16_code_units_not_code_points()
    {
        // U+E000 (one code unit 0xE000) sorts AFTER U+1F600 (surrogates 0xD83D 0xDE00) in UTF-16 order.
        var output = Jcs("{\"\\ue000\":1,\"\\ud83d\\ude00\":2}");
        Assert.Equal("{\"\U0001F600\":2,\"\uE000\":1}", output);
    }

    [Theory]
    [InlineData("\"\\u00e9\"", "\"é\"")]                 // non-ASCII as UTF-8, not \u
    [InlineData("\"\\u2028\"", "\"\u2028\"")]             // line separator is written literally
    [InlineData("\"\\u007f\"", "\"\u007f\"")]             // DEL is written literally
    [InlineData("\"\\/\"", "\"/\"")]                      // solidus is not escaped
    [InlineData("\"\\u0008\\t\\n\\u000c\\r\"", "\"\\b\\t\\n\\f\\r\"")]
    [InlineData("\"\\u0001\\u001f\"", "\"\\u0001\\u001f\"")] // other controls: lowercase \u00xx
    [InlineData("\"a\\\"b\\\\c\"", "\"a\\\"b\\\\c\"")]
    public void Escapes_only_what_RFC_8785_requires(string input, string expected) => Assert.Equal(expected, Jcs(input));

    [Fact]
    public void Lone_surrogate_is_an_error()
        => Assert.Throws<JsonCanonicalizationException>(() => Jcs("\"\\ud800\""));

    [Fact]
    public void Duplicate_member_names_are_an_error()
        => Assert.Throws<JsonCanonicalizationException>(() => Jcs("{\"a\":1,\"a\":2}"));

    [Fact]
    public void Buffer_writer_overload_writes_the_same_bytes()
    {
        using var doc = JsonDocument.Parse("{\"z\":\"é\",\"a\":[1,2]}");
        var writer = new ArrayBufferWriter<byte>();
        JsonCanonicalizer.Canonicalize(doc.RootElement, writer);
        Assert.Equal(JsonCanonicalizer.Canonicalize(doc.RootElement), writer.WrittenSpan.ToArray());
    }
}
