using System.Globalization;
using System.Numerics;

namespace Ambinder.A2A.Protocol.Signing;

internal static class JcsNumberFormatter
{
    private const long MaxSafeInteger = 9007199254740991; // 2^53 - 1

    // Temporary until the ECMAScript double formatter lands: safe integers only.
    public static string Format(JsonElement number, JcsFormat format)
    {
        var raw = number.GetRawText();
        if (raw.AsSpan().IndexOfAny('.', 'e', 'E') >= 0)
            throw new NotImplementedException("Non-integer JCS numbers are not implemented yet.");
        var big = BigInteger.Parse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if (BigInteger.Abs(big) > MaxSafeInteger)
            throw new JsonCanonicalizationException($"The number {raw} cannot be represented exactly in JCS (it is outside ±(2^53-1)). Send it as a string.");
        return big.IsZero ? "0" : big.ToString(CultureInfo.InvariantCulture);
    }
}
