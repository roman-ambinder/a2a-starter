using System.Text.Json;
using Ambinder.A2A.Protocol.Signing;

namespace Ambinder.A2A.Protocol.Tests.Signing;

public sealed class JcsVectorTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "vectors", "card-signing", "jcs");

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Dir, "*.input.json").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(f)[..^".input.json".Length]);
        Assert.NotEmpty(data);
        return data;
    }

    [Theory, MemberData(nameof(Names))]
    public void Cyberphone_pairs_canonicalize_to_the_exact_bytes(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Dir, name + ".input.json")));
        var expected = File.ReadAllBytes(Path.Combine(Dir, name + ".output.json"));
        Assert.Equal(expected, JsonCanonicalizer.Canonicalize(doc.RootElement));
    }
}
