using Ambinder.A2A.Client.Auth;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client.Tests;

public sealed class RequestOptionsTests
{
    private sealed class NoopProvider : IA2ACredentialProvider
    {
        public ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct)
            => new(new Dictionary<string, string>());
    }

    [Theory]
    [InlineData("A2A-Version", "the selected interface's protocol version")]
    [InlineData("a2a-extensions", "A2ARequestOptions.Extensions")]
    [InlineData("Content-Type", "the transport")]
    public void Reserved_headers_are_refused(string name, string hint)
    {
        var o = new A2ARequestOptions();
        var ex = Assert.Throws<ArgumentException>(() => o.Headers[name] = "x");
        Assert.Contains(name, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(hint, ex.Message);
    }

    [Fact]
    public void Reserved_headers_are_refused_through_add()
    {
        var o = new A2ARequestOptions();
        Assert.Throws<ArgumentException>(() => o.Headers.Add("A2A-Version", "1.0"));
    }

    [Fact]
    public void Authorization_is_allowed_only_without_a_credential_provider()
    {
        var o = new A2ARequestOptions { Credentials = new NoopProvider() };
        var ex = Assert.Throws<ArgumentException>(() => o.Headers["Authorization"] = "Bearer x");
        Assert.Contains("A2ARequestOptions.Credentials", ex.Message);

        var free = new A2ARequestOptions();
        free.Headers["Authorization"] = "Bearer x";   // no provider: allowed
    }

    [Fact]
    public void Validate_rechecks_authorization_when_the_client_has_a_provider()
    {
        var o = new A2ARequestOptions();
        o.Headers["Authorization"] = "Bearer x";
        o.ValidateHeaders(clientHasCredentialProvider: false);
        var ex = Assert.Throws<ArgumentException>(() => o.ValidateHeaders(clientHasCredentialProvider: true));
        Assert.Contains("Authorization", ex.Message);
    }

    [Fact]
    public void Header_values_with_line_breaks_are_refused()
    {
        var o = new A2ARequestOptions();
        Assert.Throws<ArgumentException>(() => o.Headers["X-Test"] = "a\r\nB: c");
    }

    [Fact]
    public void Headers_are_case_insensitive()
    {
        var o = new A2ARequestOptions();
        o.Headers["X-Trace"] = "1";
        Assert.Equal("1", o.Headers["x-trace"]);
        Assert.True(o.Headers.ContainsKey("X-TRACE"));
        o.Headers["x-trace"] = "2";
        Assert.Single(o.Headers);
        Assert.True(o.Headers.Remove("X-Trace"));
        Assert.Empty(o.Headers);
    }

    [Fact]
    public void Headers_hide_authorization_from_readers()
    {
        var h = new A2ARequestHeaders();
        h.SetAuthorization("Bearer secret");
        Assert.False(h.TryGetValue("Authorization", out _));
        Assert.False(h.ContainsKey("authorization"));
        Assert.DoesNotContain(h, kv => kv.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(h);
        Assert.Equal("Bearer secret", h.AuthorizationForTransport);   // internal
    }

    [Fact]
    public void Items_and_defaults()
    {
        var o = new A2ARequestOptions();
        Assert.Empty(o.Items);
        Assert.Null(o.Timeout);
        Assert.Null(o.Extensions);
        Assert.Null(o.AllowSendRetry);
        o.Items["k"] = 1;
        Assert.Equal(1, o.Items["k"]);
    }

    [Fact]
    public void Response_info_uses_init_properties()
    {
        var i = new A2AResponseInfo { Transport = "JSONRPC", ProtocolVersion = "1.0", Attempts = 2, HttpStatus = 200 };
        Assert.Empty(i.ActivatedExtensions);
        Assert.Null(i.GrpcStatus);
        Assert.Equal(2, i.Attempts);
    }
}
