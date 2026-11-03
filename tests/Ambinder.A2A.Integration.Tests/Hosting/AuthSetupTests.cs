using AgentServer.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.Integration.Tests.Hosting;

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

    private static readonly Dictionary<string, (Exception Exception, bool MetadataUnavailable)> Failures = new()
    {
        ["IDX20803 with IOException"] = (new InvalidOperationException("IDX20803: Unable to obtain configuration from: 'http://idp/x'.", new IOException("refused")), true),
        ["IDX20803"] = (new InvalidOperationException("IDX20803: Unable to obtain configuration from: 'http://idp/x'."), true),
        ["IDX20804"] = (new InvalidOperationException("IDX20804: Unable to retrieve document from: 'http://idp/x'."), true),
        ["HttpRequestException"] = (new HttpRequestException("connection refused"), true),
        ["wrapped HttpRequestException"] = (new InvalidOperationException("wrapper", new HttpRequestException("refused")), true),
        ["AggregateException of IOException"] = (new AggregateException(new IOException("reset")), true),
        ["IDX10204 issuer"] = (new Microsoft.IdentityModel.Tokens.SecurityTokenInvalidIssuerException("IDX10204: Unable to validate issuer."), true),
        ["IDX10205 issuer"] = (new Microsoft.IdentityModel.Tokens.SecurityTokenInvalidIssuerException("IDX10205: Issuer validation failed."), false),
        ["expired token"] = (new Microsoft.IdentityModel.Tokens.SecurityTokenExpiredException("IDX10223: Lifetime validation failed."), false),
        ["other InvalidOperationException"] = (new InvalidOperationException("Some other bug."), false),
        ["NullReferenceException"] = (new NullReferenceException(), false),
    };

    public static TheoryData<string> FailureNames => new(Failures.Keys);

    [Theory]
    [MemberData(nameof(FailureNames))]
    public void Only_configuration_retrieval_failures_count_as_metadata_unavailable(string name)
    {
        var (exception, expected) = Failures[name];
        Assert.Equal(expected, AuthSetup.IsMetadataUnavailable(exception));
    }
}
