using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Server.Push;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Server.Tests.Push;

public sealed class PushOptionsTests
{
    private static ValidateOptionsResult Validate(Action<PushOptions>? change = null)
    {
        var options = new PushOptions();
        change?.Invoke(options);
        return new PushOptionsValidator().Validate(null, options);
    }

    private static string Message(ValidateOptionsResult result)
    {
        Assert.True(result.Failed, "Expected validation to fail.");
        return result.FailureMessage;
    }

    [Fact]
    public void Defaults_match_spec_table()
    {
        var o = new PushOptions();
        Assert.Equal(TimeSpan.FromSeconds(15), o.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(1), o.InitialBackoff);
        Assert.Equal(TimeSpan.FromMinutes(5), o.MaxBackoff);
        Assert.Equal(10, o.MaxAttempts);
        Assert.Equal(TimeSpan.FromHours(24), o.MaxDeliveryAge);
        Assert.True(o.HonorRetryAfter);
        Assert.Equal(TimeSpan.FromSeconds(1), o.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(60), o.ClaimDuration);
        Assert.Equal(64, o.BatchSize);
        Assert.Equal(32, o.MaxConcurrentDeliveries);
        Assert.Equal(10, o.MaxConfigsPerTask);
        Assert.Equal(2048, o.MaxUrlLength);
        Assert.True(o.DeleteConfigsAfterFinalDelivery);
        Assert.Equal(TimeSpan.FromDays(7), o.DeadLetterRetention);
        Assert.Equal(100_000, o.InMemoryOutboxCapacity);
        Assert.False(o.AllowPrivateNetworks);
        Assert.Empty(o.AllowedNetworks);
        Assert.Empty(o.AllowedHosts);
        Assert.Null(o.Proxy);
        Assert.False(o.UseSystemProxy);
        Assert.False(o.ProxyEnforcesEgressPolicy);
        Assert.NotNull(o.Signature);
        Assert.Null(o.Signature.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(5), o.Signature.TokenLifetime);
        Assert.Null(o.Signature.Algorithm);
        Assert.Equal(TimeSpan.FromSeconds(10), o.ShutdownTimeout);
        Assert.True(new PushOptionsValidator().Validate(null, o).Succeeded);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(121.0)]
    public void Timeout_outside_1_to_120_seconds_fails_validation(double seconds)
    {
        var message = Message(Validate(o => o.Timeout = TimeSpan.FromSeconds(seconds)));
        Assert.Contains("A2A:Push:Timeout", message);
        Assert.Contains("1 to 120 seconds", message);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(120.0)]
    public void Timeout_at_the_range_edges_passes(double seconds) =>
        Assert.True(Validate(o => { o.Timeout = TimeSpan.FromSeconds(seconds); o.ClaimDuration = TimeSpan.FromSeconds(seconds * 2 + 1); }).Succeeded);

    [Fact]
    public void ClaimDuration_must_exceed_twice_Timeout()
    {
        var message = Message(Validate(o => { o.Timeout = TimeSpan.FromSeconds(40); o.ClaimDuration = TimeSpan.FromSeconds(60); }));
        Assert.Contains("A2A:Push:ClaimDuration", message);
        Assert.Contains("A2A:Push:Timeout", message);
        // Exactly twice is not enough: it must exceed.
        Assert.True(Validate(o => { o.Timeout = TimeSpan.FromSeconds(30); o.ClaimDuration = TimeSpan.FromSeconds(60); }).Failed);
    }

    [Fact]
    public void MaxAttempts_below_1_fails()
    {
        Assert.Contains("A2A:Push:MaxAttempts", Message(Validate(o => o.MaxAttempts = 0)));
        Assert.True(Validate(o => o.MaxAttempts = 1).Succeeded);
    }

    [Fact]
    public void BatchSize_and_MaxConcurrentDeliveries_must_be_positive()
    {
        Assert.Contains("A2A:Push:BatchSize", Message(Validate(o => o.BatchSize = 0)));
        Assert.Contains("A2A:Push:MaxConcurrentDeliveries", Message(Validate(o => o.MaxConcurrentDeliveries = -1)));
    }

    [Fact]
    public void MaxBackoff_not_below_InitialBackoff()
    {
        var message = Message(Validate(o => { o.InitialBackoff = TimeSpan.FromSeconds(10); o.MaxBackoff = TimeSpan.FromSeconds(5); }));
        Assert.Contains("A2A:Push:MaxBackoff", message);
        Assert.Contains("A2A:Push:InitialBackoff", message);
        Assert.True(Validate(o => { o.InitialBackoff = TimeSpan.FromSeconds(5); o.MaxBackoff = TimeSpan.FromSeconds(5); }).Succeeded);
    }

    [Theory]
    [InlineData("10.0.0.0/33")]
    [InlineData("not-a-cidr")]
    [InlineData("10.0.0.0")]
    [InlineData("fd00::/129")]
    [InlineData("10.0.0.0/-1")]
    public void Invalid_cidr_in_AllowedNetworks_fails(string cidr)
    {
        var message = Message(Validate(o => o.AllowedNetworks.Add(cidr)));
        Assert.Contains("A2A:Push:AllowedNetworks", message);
        Assert.Contains(cidr, message);
    }

    [Fact]
    public void Valid_cidr_in_AllowedNetworks_passes() =>
        Assert.True(Validate(o => { o.AllowedNetworks.Add("10.1.0.0/16"); o.AllowedNetworks.Add("fd00::/8"); }).Succeeded);

    [Fact]
    public void Proxy_must_be_absolute_uri()
    {
        Assert.Contains("A2A:Push:Proxy", Message(Validate(o => o.Proxy = new Uri("/proxy", UriKind.Relative))));
        Assert.True(Validate(o => o.Proxy = new Uri("http://proxy.contoso.com:3128")).Succeeded);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(601.0)]
    public void Signature_TokenLifetime_between_1s_and_10min(double seconds)
    {
        Assert.Contains("A2A:Push:Signature:TokenLifetime", Message(Validate(o => o.Signature.TokenLifetime = TimeSpan.FromSeconds(seconds))));
        Assert.True(Validate(o => o.Signature.TokenLifetime = TimeSpan.FromSeconds(1)).Succeeded);
        Assert.True(Validate(o => o.Signature.TokenLifetime = TimeSpan.FromMinutes(10)).Succeeded);
    }

    [Fact]
    public void Options_bind_from_A2A_Push_section()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["A2A:Push:MaxAttempts"] = "3",
            ["A2A:Push:Signature:Enabled"] = "false",
            ["A2A:Push:Timeout"] = "00:00:20",
            ["A2A:Push:AllowedHosts:0"] = "*.contoso.com",
            ["A2A:Push:Proxy"] = "http://proxy.contoso.com:3128",
        }).Build();

        var options = new PushOptions();
        config.GetSection("A2A:Push").Bind(options);

        Assert.Equal(3, options.MaxAttempts);
        Assert.False(options.Signature.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(20), options.Timeout);
        Assert.Equal(["*.contoso.com"], options.AllowedHosts);
        Assert.Equal(new Uri("http://proxy.contoso.com:3128"), options.Proxy);
    }

    [Fact]
    public async Task Invalid_options_fail_at_startup_when_wired_through_AddA2AServerCore()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddA2AServerCore(o =>
        {
            o.PublicBaseUrl = "http://localhost:5200";
            o.Push.MaxAttempts = 0;
        });
        await using var provider = services.BuildServiceProvider();

        // Either the startup validator or the first use of the options throws; both report the same failure.
        var ex = await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            foreach (var s in provider.GetServices<IHostedService>()) await s.StartAsync(TestContext.Current.CancellationToken);
        });
        Assert.Contains("A2A:Push:MaxAttempts", string.Join(' ', ex.Failures));
    }
}
