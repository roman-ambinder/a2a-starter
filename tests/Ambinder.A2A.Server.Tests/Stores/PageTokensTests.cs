using System.Buffers.Text;
using System.Text;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Stores;
using Xunit;

namespace Ambinder.A2A.Server.Tests.Stores;

public class PageTokensTests
{
    private static string B64(string text) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));

    private static readonly string Hash = PageTokens.FilterHash("ctx", TaskState.Working, null, false);

    [Fact]
    public void Task_token_round_trips()
    {
        var cursor = new TaskCursor(new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.FromHours(2)), "task-1");
        var token = PageTokens.EncodeTask(cursor, Hash);

        Assert.Equal(TokenResult.Ok, PageTokens.TryDecodeTask(token, Hash, out var decoded));
        Assert.Equal(cursor.StatusTimestamp.UtcTicks, decoded.StatusTimestamp.UtcTicks);
        Assert.Equal(TimeSpan.Zero, decoded.StatusTimestamp.Offset);
        Assert.Equal("task-1", decoded.TaskId);
    }

    [Fact]
    public void Task_id_containing_colons_round_trips()
    {
        var cursor = new TaskCursor(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "a:b:c");
        var token = PageTokens.EncodeTask(cursor, Hash);

        Assert.Equal(TokenResult.Ok, PageTokens.TryDecodeTask(token, Hash, out var decoded));
        Assert.Equal("a:b:c", decoded.TaskId);
    }

    [Fact]
    public void Different_filter_hash_is_a_mismatch()
    {
        var token = PageTokens.EncodeTask(new TaskCursor(DateTimeOffset.UnixEpoch, "t"), Hash);
        var other = PageTokens.FilterHash("other", TaskState.Working, null, false);

        Assert.Equal(TokenResult.FilterMismatch, PageTokens.TryDecodeTask(token, other, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!not base64url!!")]
    public void Raw_garbage_is_malformed(string token)
        => Assert.Equal(TokenResult.Malformed, PageTokens.TryDecodeTask(token, Hash, out _));

    [Fact]
    public void Structurally_wrong_tokens_are_malformed()
    {
        string[] texts =
        [
            $"v2:100:t:{Hash}",                                   // wrong prefix
            $"100:t:{Hash}",                                      // no v1 prefix
            $"v1:-1:t:{Hash}",                                    // negative ticks
            $"v1:{DateTime.MaxValue.Ticks + 1}:t:{Hash}",         // ticks too large
            $"v1:100::{Hash}",                                    // empty id
            $"v1:abc:t:{Hash}",                                   // ticks not a number
            "v1:100:t",                                           // no hash
            $"v1:100:{Hash}",                                     // missing id
        ];
        foreach (var text in texts)
            Assert.Equal(TokenResult.Malformed, PageTokens.TryDecodeTask(B64(text), Hash, out _));
    }

    [Fact]
    public void Filter_hash_is_stable_and_16_lowercase_hex_chars()
    {
        var from = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var a = PageTokens.FilterHash("c", TaskState.Completed, from, true);
        var b = PageTokens.FilterHash("c", TaskState.Completed, from, true);

        Assert.Equal(a, b);
        Assert.Matches("^[0-9a-f]{16}$", a);
    }

    [Fact]
    public void Filter_hash_changes_with_each_filter_member()
    {
        var from = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var baseline = PageTokens.FilterHash("c", TaskState.Completed, from, true);

        Assert.NotEqual(baseline, PageTokens.FilterHash("d", TaskState.Completed, from, true));
        Assert.NotEqual(baseline, PageTokens.FilterHash("c", TaskState.Failed, from, true));
        Assert.NotEqual(baseline, PageTokens.FilterHash("c", null, from, true));
        Assert.NotEqual(baseline, PageTokens.FilterHash("c", TaskState.Completed, from.AddTicks(1), true));
        Assert.NotEqual(baseline, PageTokens.FilterHash("c", TaskState.Completed, null, true));
        Assert.NotEqual(baseline, PageTokens.FilterHash("c", TaskState.Completed, from, false));
    }

    [Fact]
    public void Filter_hash_treats_equal_instants_in_different_offsets_as_equal()
    {
        var utc = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var plus2 = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal(PageTokens.FilterHash(null, null, utc, false), PageTokens.FilterHash(null, null, plus2, false));
    }

    [Fact]
    public void Push_token_round_trips()
    {
        var token = PageTokens.EncodePush(42);

        Assert.True(PageTokens.TryDecodePush(token, out var sequence));
        Assert.Equal(42, sequence);
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!")]
    public void Push_token_garbage_is_malformed(string token)
        => Assert.False(PageTokens.TryDecodePush(token, out _));

    [Fact]
    public void Push_token_with_wrong_shape_is_malformed()
    {
        Assert.False(PageTokens.TryDecodePush("v1:abc", out _));                 // not base64url-encoded text "v1:..." meaning
        Assert.False(PageTokens.TryDecodePush(B64("v1:abc"), out _));
        Assert.False(PageTokens.TryDecodePush(B64("v2:5"), out _));
        Assert.False(PageTokens.TryDecodePush(B64("v1:-5"), out _));
        Assert.False(PageTokens.TryDecodePush(B64("v1:"), out _));
    }
}
