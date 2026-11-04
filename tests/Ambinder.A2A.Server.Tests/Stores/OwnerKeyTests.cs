using Ambinder.A2A.Server.Security;
using Xunit;

namespace Ambinder.A2A.Server.Tests.Stores;

public class OwnerKeyTests
{
    [Fact]
    public void ForApp_builds_exact_string() =>
        Assert.Equal("tid:t1/app:a1", OwnerKey.ForApp("t1", "a1").Value);

    [Fact]
    public void ForUser_builds_exact_string() =>
        Assert.Equal("tid:t1/user:u1", OwnerKey.ForUser("t1", "u1").Value);

    [Fact]
    public void Parse_keeps_value() =>
        Assert.Equal("custom:x", OwnerKey.Parse("custom:x").Value);

    [Fact]
    public void Parse_empty_throws() =>
        Assert.Throws<ArgumentException>(() => OwnerKey.Parse(""));

    [Fact]
    public void Parse_null_throws() =>
        Assert.Throws<ArgumentNullException>(() => OwnerKey.Parse(null!));

    [Fact]
    public void Default_value_is_empty()
    {
        Assert.Equal("", default(OwnerKey).Value);
        Assert.Equal("", default(OwnerKey).ToString());
    }
}
