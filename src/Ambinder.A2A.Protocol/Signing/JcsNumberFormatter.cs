using System.Globalization;
using System.Numerics;

namespace Ambinder.A2A.Protocol.Signing;

internal static class JcsNumberFormatter
{
    private const long MaxSafeInteger = 9007199254740991; // 2^53 - 1

    public static string Format(JsonElement number, JcsFormat format)
    {
        var raw = number.GetRawText();
        if (raw.AsSpan().IndexOfAny('.', 'e', 'E') < 0)
        {
            var big = BigInteger.Parse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (BigInteger.Abs(big) > MaxSafeInteger)
                throw new JsonCanonicalizationException($"The number {raw} cannot be represented exactly in JCS (it is outside ±(2^53-1)). Send it as a string.");
            return big.IsZero ? "0" : big.ToString(CultureInfo.InvariantCulture);
        }
        if (!number.TryGetDouble(out var d) || !double.IsFinite(d))
            throw new JsonCanonicalizationException($"The number {raw} is not a finite IEEE 754 double.");
        return FormatDouble(d, format);
    }

    /// <summary>ECMAScript Number::toString (ECMA-262 Number.prototype.toString, radix 10), which RFC 8785 3.2.2.3 uses.</summary>
    public static string FormatDouble(double value, JcsFormat format)
    {
        if (!double.IsFinite(value)) throw new JsonCanonicalizationException("NaN and Infinity have no JSON form.");
        if (value == 0) return "0"; // also -0
        if (value < 0) return "-" + FormatDouble(-value, format);

        var (digits, n) = ShortestDigits(value); // value = 0.digits × 10^n, digits has no leading/trailing zeros
        var k = digits.Length;
        if (format == JcsFormat.Python112) return PythonRepr(digits, n);

        if (k <= n && n <= 21) return digits + new string('0', n - k);
        if (0 < n && n <= 21) return digits[..n] + "." + digits[n..];
        if (-6 < n && n <= 0) return "0." + new string('0', -n) + digits;
        var e = n - 1;
        var exp = (e < 0 ? "-" : "+") + Math.Abs(e).ToString(CultureInfo.InvariantCulture);
        return k == 1 ? digits + "e" + exp : digits[0] + "." + digits[1..] + "e" + exp;
    }

    /// <summary>Shortest round-trip digits. .NET Core 3.0+ "R" is the shortest string that round-trips (IEEE-correct).</summary>
    private static (string Digits, int N) ShortestDigits(double value)
    {
        var r = value.ToString("R", CultureInfo.InvariantCulture);
        var exp = 0;
        var ePos = r.IndexOfAny(['E', 'e']);
        if (ePos >= 0)
        {
            exp = int.Parse(r.AsSpan(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            r = r[..ePos];
        }
        var dot = r.IndexOf('.');
        var digits = dot >= 0 ? r[..dot] + r[(dot + 1)..] : r;
        var n = (dot >= 0 ? dot : r.Length) + exp;
        var lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0') lead++;
        digits = digits[lead..].TrimEnd('0');
        n -= lead;
        return (digits, n);
    }

    /// <summary>Python repr(float): exponent form when the decimal exponent is &lt; -4 or &gt;= 16; exponent has at least 2 digits.</summary>
    private static string PythonRepr(string digits, int n)
    {
        var e = n - 1;
        if (e < -4 || e >= 16)
        {
            var mant = digits.Length == 1 ? digits : digits[0] + "." + digits[1..];
            return mant + "e" + (e < 0 ? "-" : "+") + Math.Abs(e).ToString("00", CultureInfo.InvariantCulture);
        }
        if (n <= 0) return "0." + new string('0', -n) + digits;
        if (n >= digits.Length) return digits + new string('0', n - digits.Length) + ".0";
        return digits[..n] + "." + digits[n..];
    }
}
