using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Stores;

/// <summary>The outcome of decoding a page token.</summary>
internal enum TokenResult
{
    /// <summary>The token is valid for this query.</summary>
    Ok,
    /// <summary>The token is not a token this core made.</summary>
    Malformed,
    /// <summary>The token is valid but was made for a different filter.</summary>
    FilterMismatch,
}

/// <summary>Page tokens owned by the core. Not signed on purpose: scope comes from the call, never from the token (§7.4).</summary>
internal static class PageTokens
{
    private const string Version = "v1";

    // Task token: base64url("v1:{utcTicks}:{taskId}:{filterHash}") (spec §7.4 order). filterHash is 16 lowercase hex chars.
    // Parse: "v1" up to the first ':', ticks up to the second ':', hash = the text after the LAST ':', id = everything between.
    // So a task id that contains ':' still round-trips.

    /// <summary>Encodes a ListTasks cursor together with the hash of the filter it belongs to.</summary>
    public static string EncodeTask(TaskCursor cursor, string filterHash)
        => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            $"{Version}:{cursor.StatusTimestamp.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{cursor.TaskId}:{filterHash}"));

    /// <summary>Decodes a ListTasks token and checks it was made for <paramref name="expectedFilterHash"/>.</summary>
    public static TokenResult TryDecodeTask(string token, string expectedFilterHash, out TaskCursor cursor)
    {
        cursor = default;
        if (!TryDecodeText(token, out var text)) return TokenResult.Malformed;

        var first = text.IndexOf(':');
        if (first < 0 || !text.AsSpan(0, first).SequenceEqual(Version)) return TokenResult.Malformed;
        var second = text.IndexOf(':', first + 1);
        if (second < 0) return TokenResult.Malformed;
        var last = text.LastIndexOf(':');
        if (last <= second + 1 || last == text.Length - 1) return TokenResult.Malformed; // empty id or empty hash

        if (!long.TryParse(text.AsSpan(first + 1, second - first - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) return TokenResult.Malformed;
        if (ticks < 0 || ticks > DateTime.MaxValue.Ticks) return TokenResult.Malformed;

        var id = text[(second + 1)..last];
        var hash = text.AsSpan(last + 1);
        if (!hash.SequenceEqual(expectedFilterHash)) return TokenResult.FilterMismatch;

        cursor = new TaskCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return TokenResult.Ok;
    }

    /// <summary>First 8 bytes of SHA-256 over the canonical filter, as 16 lowercase hex chars.</summary>
    public static string FilterHash(string? contextId, TaskState? state, DateTimeOffset? from, bool includeArtifacts)
    {
        var canonical = "ctx=" + (contextId ?? "")
            + "\nstate=" + ((int?)state)?.ToString(CultureInfo.InvariantCulture)
            + "\nfrom=" + from?.UtcTicks.ToString(CultureInfo.InvariantCulture)
            + "\nart=" + (includeArtifacts ? "1" : "0");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    // Push token: base64url("v1:{sequence}")

    /// <summary>Encodes a push-config list cursor.</summary>
    public static string EncodePush(long afterSequence)
        => Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{Version}:{afterSequence.ToString(CultureInfo.InvariantCulture)}"));

    /// <summary>Decodes a push-config list cursor. False for anything that is not a valid token.</summary>
    public static bool TryDecodePush(string token, out long afterSequence)
    {
        afterSequence = 0;
        if (!TryDecodeText(token, out var text)) return false;
        if (!text.StartsWith(Version + ":", StringComparison.Ordinal)) return false;
        if (!long.TryParse(text.AsSpan(Version.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)) return false;
        afterSequence = sequence;
        return true;
    }

    private static bool TryDecodeText(string token, out string text)
    {
        text = "";
        if (string.IsNullOrEmpty(token) || !Base64Url.IsValid(token)) return false;
        try { text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(Base64Url.DecodeFromChars(token)); }
        catch (Exception e) when (e is FormatException or ArgumentException) { return false; }
        return true;
    }
}
