namespace Ambinder.A2A.Server.Push;

/// <summary>The core default: stores credentials unprotected. A startup check warns about it outside Development.</summary>
internal sealed class PassThroughPushCredentialProtector : IPushCredentialProtector
{
    public string Protect(string plaintext) => plaintext;

    public string Unprotect(string protectedValue) => protectedValue;
}
