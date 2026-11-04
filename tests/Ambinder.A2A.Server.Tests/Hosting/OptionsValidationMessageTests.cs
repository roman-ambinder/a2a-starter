using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Server.Tests.Hosting;

/// <summary>
/// Principle 3: a validation failure names the property, shows the bad value and says what to set. The options are
/// checked in AddA2AServerCore, so they are checked with and without ASP.NET Core.
/// </summary>
public class OptionsValidationMessageTests
{
    private static OptionsValidationException Resolve(Action<A2AServerOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddA2AServerCore(configure);
        using var provider = services.BuildServiceProvider();
        return Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<A2AServerOptions>>().Value);
    }

    [Fact]
    public void Options_validation_runs_in_AddA2AServerCore()
    {
        var services = new ServiceCollection();
        services.AddA2AServerCore(o => o.PublicBaseUrl = "ftp://x");
        Assert.Contains(services, d => d.ServiceType == typeof(IValidateOptions<A2AServerOptions>));
    }

    [Theory]
    [InlineData("ftp://x", "'ftp://x'")]
    [InlineData("not a url", "'not a url'")]
    [InlineData("", "empty")]
    public void PublicBaseUrl_message_names_the_property_the_value_and_the_fix(string value, string shown)
    {
        var ex = Resolve(o => o.PublicBaseUrl = value);

        var message = Assert.Single(ex.Failures);
        Assert.Contains("A2AServerOptions.PublicBaseUrl", message);
        Assert.Contains(shown, message);
        Assert.Contains("Set PublicBaseUrl", message);
        Assert.Contains("https://agents.contoso.com", message);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void MaxRequestBodyBytes_message_names_the_property_the_value_and_the_fix(long value)
    {
        var ex = Resolve(o =>
        {
            o.PublicBaseUrl = "http://localhost:5200";
            o.MaxRequestBodyBytes = value;
        });

        var message = Assert.Single(ex.Failures);
        Assert.Contains("A2AServerOptions.MaxRequestBodyBytes", message);
        Assert.Contains($"is {value}", message);
        Assert.Contains("Set MaxRequestBodyBytes", message);
        Assert.Contains("4194304", message);
    }

    [Fact]
    public void Both_failures_are_reported_together()
    {
        var ex = Resolve(o =>
        {
            o.PublicBaseUrl = "";
            o.MaxRequestBodyBytes = 0;
        });

        Assert.Equal(2, ex.Failures.Count());
    }

    [Fact]
    public void Valid_options_pass()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddA2AServerCore(o => o.PublicBaseUrl = "https://agents.contoso.com");
        using var provider = services.BuildServiceProvider();
        Assert.Equal("https://agents.contoso.com", provider.GetRequiredService<IOptions<A2AServerOptions>>().Value.PublicBaseUrl);
    }

    [Fact]
    public void Unknown_DefaultAgent_message_lists_the_agents_and_says_what_to_set()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddA2AServerCore(o =>
        {
            o.PublicBaseUrl = "http://localhost:5200";
            o.DefaultAgent = "missing";
        }).AddAgent<ScriptedAgent>("echo", _ => new AgentCard { Name = "echo" });
        services.AddSingleton<AgentScripts>();
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<AgentCatalog>());

        Assert.Contains("A2AServerOptions.DefaultAgent is 'missing'", ex.Message);
        Assert.Contains("'echo'", ex.Message);
        Assert.Contains("Set DefaultAgent to the name of an agent you added with AddAgent", ex.Message);
    }
}
