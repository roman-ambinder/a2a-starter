using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Ambinder.A2A.Server.Tasks;

/// <summary>Opaque cursor for ListTasks: the (status timestamp, task id) of the last item returned.</summary>
internal static class PageToken
{
    /// <summary>Encodes a cursor as base64url of <c>utcTicks:id</c>.</summary>
    public static string Encode(DateTimeOffset timestamp, string taskId)
        => Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{timestamp.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{taskId}"));

    /// <summary>Decodes a cursor. Returns false for anything that is not a valid token.</summary>
    public static bool TryDecode(string token, out DateTimeOffset timestamp, out string taskId)
    {
        timestamp = default;
        taskId = "";
        if (string.IsNullOrEmpty(token) || !Base64Url.IsValid(token)) return false;
        string text;
        try { text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token)); }
        catch (FormatException) { return false; }
        var colon = text.IndexOf(':');
        if (colon <= 0 || colon == text.Length - 1) return false;
        if (!long.TryParse(text.AsSpan(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) return false;
        if (ticks < 0 || ticks > DateTime.MaxValue.Ticks) return false;
        timestamp = new DateTimeOffset(ticks, TimeSpan.Zero);
        taskId = text[(colon + 1)..];
        return true;
    }
}
