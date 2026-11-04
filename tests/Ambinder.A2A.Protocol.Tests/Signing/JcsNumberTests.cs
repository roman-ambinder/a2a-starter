using System.Globalization;
using System.Text;
using System.Text.Json;
using Ambinder.A2A.Protocol.Signing;

namespace Ambinder.A2A.Protocol.Tests.Signing;

public sealed class JcsNumberTests
{
    private static string Num(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Encoding.UTF8.GetString(JsonCanonicalizer.Canonicalize(doc.RootElement));
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("-0", "0")]
    [InlineData("1", "1")]
    [InlineData("-1.5", "-1.5")]
    [InlineData("1e-6", "0.000001")]
    [InlineData("1e-7", "1e-7")]
    [InlineData("1e20", "100000000000000000000")]
    [InlineData("1e21", "1e+21")]
    [InlineData("123.456", "123.456")]
    [InlineData("1.0", "1")]
    [InlineData("100", "100")]
    [InlineData("9007199254740991", "9007199254740991")]
    [InlineData("4.5e15", "4500000000000000")]
    public void Formats_like_ECMAScript(string input, string expected) => Assert.Equal(expected, Num(input));

    [Theory]
    [InlineData("9007199254740992")]
    [InlineData("-9007199254740992")]
    [InlineData("12345678901234567890")]
    public void Integers_outside_the_safe_range_are_an_error(string input)
    {
        var ex = Assert.Throws<JsonCanonicalizationException>(() => Num(input));
        Assert.Contains("cannot be represented exactly in JCS", ex.Message);
    }

    [Fact]
    public void Overflow_is_an_error() => Assert.Throws<JsonCanonicalizationException>(() => Num("1e400"));

    public static TheoryData<string, string> RfcSamples()
    {
        var data = new TheoryData<string, string>();
        var path = Path.Combine(AppContext.BaseDirectory, "vectors", "card-signing", "jcs", "numbers.txt");
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0) continue;
            var parts = line.Split(' ', 2);
            data.Add(parts[0], parts[1]);
        }
        Assert.NotEmpty(data);
        return data;
    }

    [Theory, MemberData(nameof(RfcSamples))]
    public void RFC_8785_appendix_B(string ieeeHex, string expected)
    {
        var d = BitConverter.Int64BitsToDouble(long.Parse(ieeeHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        if (expected is "NaN" or "Infinity")
        {
            Assert.Throws<JsonCanonicalizationException>(() => JcsNumberFormatter.FormatDouble(d, JcsFormat.Rfc8785));
            return;
        }
        Assert.Equal(expected, JcsNumberFormatter.FormatDouble(d, JcsFormat.Rfc8785));
    }
}
