using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Push;

namespace Ambinder.A2A.Server.Tests.Push;

public sealed class WebhookUrlValidatorTests
{
    private static Task Validate(string url, Action<PushOptions>? change = null)
    {
        var options = new PushOptions();
        change?.Invoke(options);
        return WebhookUrlValidator.ValidateAsync(new Uri(url), new PushAddressPolicy(options), TestContext.Current.CancellationToken).AsTask();
    }

    private static async Task Refused(string url, Action<PushOptions>? change = null)
    {
        var ex = await Assert.ThrowsAsync<A2AException>(() => Validate(url, change));
        Assert.Equal(A2AErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public async Task Url_longer_than_MaxUrlLength_refused()
    {
        await Refused("https://93.184.216.34/" + new string('a', 100), o => o.MaxUrlLength = 50);
        await Validate("https://93.184.216.34/hook", o => o.MaxUrlLength = 50);
    }

    [Fact]
    public async Task Fragment_refused() => await Refused("https://93.184.216.34/hook#frag");

    [Theory]
    [InlineData("https://93.184.216.34:22/hook", false)]
    [InlineData("https://93.184.216.34:25/hook", false)]
    [InlineData("https://93.184.216.34:443/hook", true)]
    [InlineData("http://93.184.216.34:80/hook", true)]
    [InlineData("https://93.184.216.34:8443/hook", true)]
    public async Task Port_below_1024_other_than_80_and_443_refused(string url, bool accepted)
    {
        if (accepted) await Validate(url, o => o.AllowPrivateNetworks = true);
        else await Refused(url, o => o.AllowPrivateNetworks = true);
    }

    [Fact]
    public async Task Host_not_in_AllowedHosts_refused()
    {
        await Refused("https://93.184.216.34/hook", o => o.AllowedHosts.Add("hooks.contoso.com"));
        await Validate("https://hooks.contoso.com/hook", o => { o.AllowPrivateNetworks = true; o.AllowedHosts.Add("hooks.contoso.com"); });
        await Validate("https://HOOKS.contoso.com/hook", o => { o.AllowPrivateNetworks = true; o.AllowedHosts.Add("hooks.contoso.com"); });
    }

    [Fact]
    public async Task Wildcard_AllowedHosts_needs_dot_boundary()
    {
        void Configure(PushOptions o) { o.AllowPrivateNetworks = true; o.AllowedHosts.Add("*.contoso.com"); }
        await Validate("https://a.contoso.com/hook", Configure);
        await Validate("https://a.b.contoso.com/hook", Configure);
        await Refused("https://evilcontoso.com/hook", Configure);
        await Refused("https://contoso.com.evil.example/hook", Configure);
        await Refused("https://contoso.com/hook", Configure);
    }

    [Fact]
    public async Task Private_address_in_AllowedNetworks_accepted()
        => await Validate("https://10.1.2.3/hook", o => o.AllowedNetworks.Add("10.0.0.0/8"));

    [Fact]
    public async Task Private_address_outside_AllowedNetworks_refused()
    {
        await Refused("https://192.168.1.5/hook", o => o.AllowedNetworks.Add("10.0.0.0/8"));
        await Refused("https://10.1.2.3/hook", o => o.AllowedNetworks.Add("10.2.0.0/16"));
    }

    [Fact]
    public async Task Ipv6_unique_local_in_AllowedNetworks_accepted()
    {
        await Validate("https://[fd00::5]/hook", o => o.AllowedNetworks.Add("fd00::/8"));
        await Refused("https://[fd00::5]/hook", o => o.AllowedNetworks.Add("fc00:1::/32"));
    }

    [Fact]
    public async Task Allowed_network_does_not_open_other_private_ranges_or_public_http()
    {
        await Refused("https://127.0.0.1/hook", o => o.AllowedNetworks.Add("10.0.0.0/8"));
        await Refused("http://10.1.2.3/hook", o => o.AllowedNetworks.Add("10.0.0.0/8"));
    }

    [Fact]
    public async Task Existing_rules_kept()
    {
        await Refused("http://93.184.216.34/hook");
        await Refused("https://127.0.0.1/hook");
        await Refused("https://user:pw@93.184.216.34/hook");
        await Refused("https://no-such-host.invalid/hook");
    }

    [Fact]
    public void IsAllowed_unwraps_ipv4_mapped_addresses()
    {
        var options = new PushOptions();
        options.AllowedNetworks.Add("10.0.0.0/8");
        var policy = new PushAddressPolicy(options);
        Assert.True(policy.IsAllowed(System.Net.IPAddress.Parse("::ffff:10.1.2.3")));
        Assert.False(policy.IsAllowed(System.Net.IPAddress.Parse("::ffff:127.0.0.1")));
    }
}
