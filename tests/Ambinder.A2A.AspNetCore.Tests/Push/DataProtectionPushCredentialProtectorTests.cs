using Ambinder.A2A.AspNetCore.Push;
using Ambinder.A2A.Server.Push;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.AspNetCore.Tests.Push;

public sealed class DataProtectionPushCredentialProtectorTests
{
    private static IDataProtectionProvider NewProvider()
        => new EphemeralDataProtectionProvider();

    [Fact]
    public void DataProtection_round_trips()
    {
        var p = new DataProtectionPushCredentialProtector(NewProvider());
        Assert.Equal("s3cret-token", p.Unprotect(p.Protect("s3cret-token")));
    }

    [Fact]
    public void DataProtection_output_does_not_contain_plaintext()
    {
        var p = new DataProtectionPushCredentialProtector(NewProvider());
        Assert.DoesNotContain("s3cret-token", p.Protect("s3cret-token"));
    }

    [Fact]
    public void DataProtection_uses_purpose_Ambinder_A2A_Push_Credentials()
    {
        var provider = NewProvider();
        var value = new DataProtectionPushCredentialProtector(provider).Protect("s3cret-token");

        Assert.Equal("s3cret-token", provider.CreateProtector("Ambinder.A2A.Push.Credentials").Unprotect(value));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => provider.CreateProtector("Other.Purpose").Unprotect(value));
    }

    [Fact]
    public void AspNetCore_registration_replaces_pass_through()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddA2AServer(o => o.PublicBaseUrl = "http://localhost:5200");
        using var provider = services.BuildServiceProvider();

        Assert.IsType<DataProtectionPushCredentialProtector>(provider.GetRequiredService<IPushCredentialProtector>());
    }
}
