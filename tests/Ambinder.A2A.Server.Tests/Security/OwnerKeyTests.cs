using Ambinder.A2A.Server.Security;

namespace Ambinder.A2A.Server.Tests.Security;

public class OwnerKeyTests
{
    /// <summary>
    /// Golden test. The two string literals below are the frozen 1.0 owner key format.
    /// Changing them breaks every stored task, see spec 07 B7.
    /// </summary>
    [Fact]
    public void Golden_format_is_frozen()
    {
        Assert.Equal("tid:t1/app:a1", OwnerKey.ForApp("t1", "a1").Value);
        Assert.Equal("tid:t1/user:u1", OwnerKey.ForUser("t1", "u1").Value);
    }

    [Fact]
    public void ToString_returns_value()
        => Assert.Equal("tid:t1/app:a1", OwnerKey.ForApp("t1", "a1").ToString());

    [Theory]
    [InlineData("apikey:ops")]
    [InlineData("tid:t1/app:a1")]
    [InlineData("tid:t1/user:u1")]
    public void Parse_keeps_the_value(string value)
        => Assert.Equal(value, OwnerKey.Parse(value).Value);

    [Fact]
    public void Parse_round_trips_both_formats()
    {
        var app = OwnerKey.ForApp("t1", "a1");
        var user = OwnerKey.ForUser("t1", "u1");

        Assert.Equal(app, OwnerKey.Parse(app.Value));
        Assert.Equal(user, OwnerKey.Parse(user.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Null_or_empty_arguments_throw(string? bad)
    {
        Assert.ThrowsAny<ArgumentException>(() => OwnerKey.ForApp(bad!, "a1"));
        Assert.ThrowsAny<ArgumentException>(() => OwnerKey.ForApp("t1", bad!));
        Assert.ThrowsAny<ArgumentException>(() => OwnerKey.ForUser(bad!, "u1"));
        Assert.ThrowsAny<ArgumentException>(() => OwnerKey.ForUser("t1", bad!));
        Assert.ThrowsAny<ArgumentException>(() => OwnerKey.Parse(bad!));
    }

    [Fact]
    public void A_slash_in_tenant_or_id_throws()
    {
        Assert.Throws<ArgumentException>(() => OwnerKey.ForApp("t/1", "a1"));
        Assert.Throws<ArgumentException>(() => OwnerKey.ForApp("t1", "a/1"));
        Assert.Throws<ArgumentException>(() => OwnerKey.ForUser("t/1", "u1"));
        Assert.Throws<ArgumentException>(() => OwnerKey.ForUser("t1", "u/1"));
    }

    [Fact]
    public void Value_of_256_chars_is_accepted_and_257_throws()
    {
        Assert.Equal(256, OwnerKey.Parse(new string('x', 256)).Value.Length);
        Assert.Throws<ArgumentException>(() => OwnerKey.Parse(new string('x', 257)));
    }

    [Fact]
    public void Built_keys_longer_than_256_chars_throw()
    {
        Assert.Throws<ArgumentException>(() => OwnerKey.ForApp(new string('t', 200), new string('a', 100)));
        Assert.Throws<ArgumentException>(() => OwnerKey.ForUser(new string('t', 200), new string('u', 100)));
    }

    [Fact]
    public void Equality_is_ordinal_on_value()
    {
        Assert.Equal(OwnerKey.ForApp("t1", "a1"), OwnerKey.Parse("tid:t1/app:a1"));
        Assert.Equal(OwnerKey.ForApp("t1", "a1").GetHashCode(), OwnerKey.Parse("tid:t1/app:a1").GetHashCode());
        Assert.NotEqual(OwnerKey.ForApp("t1", "a1"), OwnerKey.ForApp("T1", "a1"));
        Assert.NotEqual(OwnerKey.ForApp("t1", "a1"), OwnerKey.ForUser("t1", "a1"));
    }
}
