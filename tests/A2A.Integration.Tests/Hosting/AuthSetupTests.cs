using AgentServer.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace A2A.Integration.Tests.Hosting;

/// <summary>A plain-HTTP authority with RequireHttpsMetadata=true would make every request fail with 500; it must stop startup instead.</summary>
public sealed class AuthSetupTests
{
    private static IConfiguration Config(string authority, bool requireHttps) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Authority"] = authority,
            ["Auth:Audience"] = "api://a2a-demo",
            ["Auth:RequireHttpsMetadata"] = requireHttps.ToString(),
        }).Build();

    [Fact]
    public void Http_authority_with_RequireHttpsMetadata_fails_fast_with_a_hint()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddA2AAuth(Config("http://localhost:5100/t/v2.0", requireHttps: true)));
        Assert.Contains("ASPNETCORE_ENVIRONMENT=Development", ex.Message);
        Assert.Contains("https", ex.Message);
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/t/v2.0", true)]
    [InlineData("http://localhost:5100/t/v2.0", false)]
    public void Valid_combinations_register(string authority, bool requireHttps)
        => new ServiceCollection().AddA2AAuth(Config(authority, requireHttps));
}
