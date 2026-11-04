using Ambinder.A2A.Server.Push;
using Microsoft.AspNetCore.DataProtection;

namespace Ambinder.A2A.AspNetCore.Push;

/// <summary>Protects push credentials with ASP.NET Core Data Protection. Keys must be shared between all pods that read the same store.</summary>
internal sealed class DataProtectionPushCredentialProtector : IPushCredentialProtector
{
    internal const string Purpose = "Ambinder.A2A.Push.Credentials";

    private readonly IDataProtector _protector;

    public DataProtectionPushCredentialProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return _protector.Protect(plaintext);
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        return _protector.Unprotect(protectedValue);
    }
}
