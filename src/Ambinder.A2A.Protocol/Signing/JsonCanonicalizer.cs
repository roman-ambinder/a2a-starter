using System.Buffers;
using System.Text;

namespace Ambinder.A2A.Protocol.Signing;

/// <summary>
/// RFC 8785 JSON Canonicalization Scheme (JCS): the byte form that A2A Agent Card signatures cover.
/// </summary>
/// <remarks>All members are thread-safe. The canonicalizer keeps no state between calls.</remarks>
public static class JsonCanonicalizer
{
    /// <summary>Returns the JCS form of a JSON value as UTF-8 bytes.</summary>
    /// <param name="value">The JSON value to canonicalize.</param>
    /// <returns>The canonical UTF-8 bytes, without a byte order mark.</returns>
    /// <exception cref="JsonCanonicalizationException">The value has no JCS form.</exception>
    public static byte[] Canonicalize(JsonElement value)
    {
        var writer = new ArrayBufferWriter<byte>(1024);
        Canonicalize(value, writer);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>Writes the JCS form of a JSON value to a buffer writer.</summary>
    /// <param name="value">The JSON value to canonicalize.</param>
    /// <param name="output">The buffer that receives the canonical UTF-8 bytes.</param>
    /// <exception cref="JsonCanonicalizationException">The value has no JCS form. The buffer may hold partial output.</exception>
    public static void Canonicalize(JsonElement value, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        WriteValue(value, output, JcsFormat.Rfc8785);
    }

    internal static void WriteValue(JsonElement value, IBufferWriter<byte> o, JcsFormat format)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object: WriteObject(value, o, format); break;
            case JsonValueKind.Array:
                Ascii(o, "[");
                var first = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!first) Ascii(o, ",");
                    first = false;
                    WriteValue(item, o, format);
                }
                Ascii(o, "]");
                break;
            case JsonValueKind.String: WriteString(GetString(value), o, format); break;
            case JsonValueKind.Number: Ascii(o, JcsNumberFormatter.Format(value, format)); break;
            case JsonValueKind.True: Ascii(o, "true"); break;
            case JsonValueKind.False: Ascii(o, "false"); break;
            case JsonValueKind.Null: Ascii(o, "null"); break;
            default: throw new JsonCanonicalizationException($"Unexpected JSON value kind {value.ValueKind}. Pass a parsed JSON value, not an undefined element.");
        }
    }

    private static void WriteObject(JsonElement value, IBufferWriter<byte> o, JcsFormat format)
    {
        var members = new List<(string Name, JsonElement Value)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in value.EnumerateObject())
        {
            if (!seen.Add(p.Name))
                throw new JsonCanonicalizationException($"Duplicate member name '{p.Name}'. JCS needs I-JSON (RFC 7493), which forbids duplicates. Remove one of them.");
            members.Add((p.Name, p.Value));
        }
        members.Sort((a, b) => CompareNames(a.Name, b.Name, format));
        Ascii(o, "{");
        for (var i = 0; i < members.Count; i++)
        {
            if (i > 0) Ascii(o, ",");
            WriteString(members[i].Name, o, format);
            Ascii(o, ":");
            WriteValue(members[i].Value, o, format);
        }
        Ascii(o, "}");
    }

    /// <summary>RFC 8785 3.2.3: UTF-16 code units. The Python 1.1.2 variant sorts by code point.</summary>
    internal static int CompareNames(string a, string b, JcsFormat format)
    {
        if (format != JcsFormat.Python112) return string.CompareOrdinal(a, b);
        var ea = a.EnumerateRunes();
        var eb = b.EnumerateRunes();
        while (true)
        {
            var ha = ea.MoveNext();
            var hb = eb.MoveNext();
            if (!ha || !hb) return ha == hb ? 0 : ha ? 1 : -1;
            var c = ea.Current.Value.CompareTo(eb.Current.Value);
            if (c != 0) return c;
        }
    }

    internal static string GetString(JsonElement value)
    {
        try { return value.GetString()!; }
        catch (InvalidOperationException ex) { throw new JsonCanonicalizationException($"A string is not valid UTF-16: {ex.Message}"); }
    }

    internal static void WriteString(string s, IBufferWriter<byte> o, JcsFormat format)
    {
        Ascii(o, "\"");
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var escape = c switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                < ' ' => $"\\u{(int)c:x4}",
                _ => null,
            };
            if (escape is null && char.IsSurrogate(c))
            {
                if (!char.IsHighSurrogate(c) || i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))
                    throw new JsonCanonicalizationException("A string has a lone surrogate. JCS needs valid Unicode. Remove or replace the unpaired surrogate.");
                if (format != JcsFormat.Python112) { i++; continue; }
                Utf8(o, s.AsSpan(start, i - start));
                Ascii(o, $"\\u{(int)c:x4}\\u{(int)s[i + 1]:x4}");
                i++;
                start = i + 1;
                continue;
            }
            // Python json.dumps(ensure_ascii=True) escapes everything outside 0x20..0x7E.
            if (escape is null && format == JcsFormat.Python112 && c > '~') escape = $"\\u{(int)c:x4}";
            if (escape is null) continue;
            Utf8(o, s.AsSpan(start, i - start));
            Ascii(o, escape);
            start = i + 1;
        }
        Utf8(o, s.AsSpan(start));
        Ascii(o, "\"");
    }

    internal static void Ascii(IBufferWriter<byte> o, string s)
    {
        var span = o.GetSpan(s.Length);
        for (var i = 0; i < s.Length; i++) span[i] = (byte)s[i];
        o.Advance(s.Length);
    }

    private static void Utf8(IBufferWriter<byte> o, ReadOnlySpan<char> s)
    {
        if (s.IsEmpty) return;
        var span = o.GetSpan(Encoding.UTF8.GetMaxByteCount(s.Length));
        o.Advance(Encoding.UTF8.GetBytes(s, span));
    }
}

/// <summary>Output dialect. Only <see cref="Rfc8785"/> is JCS; the other exists to diagnose cards signed by Python SDK 1.1.2.</summary>
internal enum JcsFormat { Rfc8785, Python112 }
