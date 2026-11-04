using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Push;

namespace Ambinder.A2A.Server.Tests.Push;

public sealed class PushConfigValidationTests
{
    private const string Url = "https://93.184.216.34/hook";

    private static PushConfigService Service(Action<PushOptions>? change = null)
    {
        var options = new PushOptions();
        change?.Invoke(options);
        return new PushConfigService(new InMemoryPushConfigStore(), options);
    }

    private static TaskPushNotificationConfig Config(string? scheme = null, string? credentials = null, string? token = null) => new()
    {
        Url = Url,
        Token = token,
        Authentication = scheme is null ? null : new AuthenticationInfo { Scheme = scheme, Credentials = credentials },
    };

    private static async Task<A2AException> Refused(TaskPushNotificationConfig config, string secret)
    {
        var ex = await Assert.ThrowsAsync<A2AException>(async () => await Service().ValidateAsync(config, TestContext.Current.CancellationToken));
        Assert.Equal(A2AErrorCode.InvalidParams, ex.Code);
        Assert.DoesNotContain(secret, ex.Message, StringComparison.Ordinal);
        return ex;
    }

    [Theory]
    [InlineData("Bearer")]
    [InlineData("basic")]
    [InlineData("X-Custom")]
    public async Task Valid_scheme_accepted(string scheme)
        => await Service().ValidateAsync(Config(scheme, "abc"), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("")]
    [InlineData("Bearer x")]
    [InlineData("Bea\"rer")]
    [InlineData("Bearer\r\nX-Evil: 1")]
    public async Task Scheme_must_be_rfc9110_token(string scheme) => await Refused(Config(scheme, "abc"), "abc");

    [Fact]
    public async Task Scheme_longer_than_64_chars_refused()
    {
        await Service().ValidateAsync(Config(new string('a', 64)), TestContext.Current.CancellationToken);
        await Refused(Config(new string('a', 65)), new string('a', 65));
    }

    [Theory]
    [InlineData("abc\r\nX-Evil: 1")]
    [InlineData("\u0000")]
    [InlineData("\u007f")]
    [InlineData("abc\tdef")]
    public async Task Credentials_with_CR_LF_or_control_chars_refused(string credentials)
        => await Refused(Config("Bearer", credentials), credentials);

    [Fact]
    public async Task Credentials_with_printable_chars_accepted()
        => await Service().ValidateAsync(Config("Bearer", "eyJhbGciOi.J9-_~+/="), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("tok\r\nX-Evil: 1")]
    [InlineData("tok\nen")]
    [InlineData("tok\u0000en")]
    public async Task Token_with_CR_LF_refused(string token) => await Refused(Config(token: token), token);

    [Fact]
    public async Task Bad_url_is_refused_without_echoing_credentials()
    {
        var config = Config("Bearer", "s3cret") with { Url = "http://93.184.216.34/hook" };
        await Refused(config, "s3cret");
    }

    [Fact]
    public async Task Relative_url_refused()
        => await Refused(Config() with { Url = "/hook" }, "s3cret");

    // Blocked until package 05 lands (no TaskRef, StoredPushConfig.ProtocolVersion or new IPushConfigStore):
    // Unknown_protocol_version_refused, Null_protocol_version_means_1_0, MaxConfigsPerTask_enforced.
}
