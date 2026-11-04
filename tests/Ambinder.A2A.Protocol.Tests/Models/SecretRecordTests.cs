namespace Ambinder.A2A.Protocol.Tests.Models;

public sealed class SecretRecordTests
{
    private const string Secret = "s3cr3t-value";

    [Fact]
    public void AuthenticationInfo_ToString_hides_credentials()
    {
        var text = new AuthenticationInfo { Scheme = "Bearer", Credentials = Secret }.ToString();
        Assert.DoesNotContain(Secret, text);
        Assert.Contains("Bearer", text);
        Assert.Contains("***", text);
    }

    [Fact]
    public void AuthenticationInfo_ToString_shows_null_when_no_credentials()
    {
        var text = new AuthenticationInfo { Scheme = "Bearer" }.ToString();
        Assert.DoesNotContain("***", text);
        Assert.Contains("Credentials = null", text);
    }

    [Fact]
    public void TaskPushNotificationConfig_ToString_hides_token_and_credentials()
    {
        var config = new TaskPushNotificationConfig
        {
            Url = "https://example.test/hook",
            Token = Secret,
            Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = Secret + "-2" },
        };
        var text = config.ToString();
        Assert.DoesNotContain(Secret, text);
        Assert.Contains("https://example.test/hook", text);
    }

    [Fact]
    public void Equality_still_compares_secret_members()
    {
        Assert.NotEqual(new AuthenticationInfo { Scheme = "Bearer", Credentials = "a" }, new AuthenticationInfo { Scheme = "Bearer", Credentials = "b" });
        Assert.NotEqual(new TaskPushNotificationConfig { Url = "u", Token = "a" }, new TaskPushNotificationConfig { Url = "u", Token = "b" });
    }
}
