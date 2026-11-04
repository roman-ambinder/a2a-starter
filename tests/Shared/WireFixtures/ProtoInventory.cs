using System.Text.RegularExpressions;

namespace Ambinder.A2A.Tests.WireFixtures;

/// <summary>Top-level messages and enums (with values) of a proto3 file. Test code; regex is enough for a2a.proto.</summary>
public sealed partial class ProtoInventory
{
    public required IReadOnlyList<string> Messages { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Enums { get; init; }

    public static ProtoInventory Load(string path)
    {
        var text = File.ReadAllText(path);
        var messages = TopMessage().Matches(text).Select(m => m.Groups[1].Value).ToList();
        var enums = new Dictionary<string, IReadOnlyList<string>>();
        foreach (Match e in TopEnum().Matches(text))
            enums[e.Groups[1].Value] = EnumValue().Matches(e.Groups[2].Value).Select(v => v.Groups[1].Value).ToList();
        return new ProtoInventory { Messages = messages, Enums = enums };
    }

    [GeneratedRegex(@"^message (\w+) \{", RegexOptions.Multiline)] private static partial Regex TopMessage();
    [GeneratedRegex(@"^enum (\w+) \{(.*?)^\}", RegexOptions.Multiline | RegexOptions.Singleline)] private static partial Regex TopEnum();
    [GeneratedRegex(@"^\s+([A-Z][A-Z0-9_]+)\s*=", RegexOptions.Multiline)] private static partial Regex EnumValue();
}
