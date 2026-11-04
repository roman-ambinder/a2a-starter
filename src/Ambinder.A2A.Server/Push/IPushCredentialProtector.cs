namespace Ambinder.A2A.Server.Push;

/// <summary>Protects push credentials (<c>authentication.credentials</c> and <c>token</c>) before a durable push-config store writes them.</summary>
/// <remarks>
/// Lifetime: registered as a singleton. Concurrency: calls can overlap, so an implementation must be thread-safe.
/// The default in <c>Ambinder.A2A.Server</c> returns the value unchanged; <c>Ambinder.A2A.AspNetCore</c> replaces it with ASP.NET Core Data Protection.
/// <see cref="Unprotect"/> must accept every value that <see cref="Protect"/> returned, including values from earlier keys.
/// Keep the keys shared between all pods that read the same store.
/// </remarks>
public interface IPushCredentialProtector
{
    /// <summary>Returns the protected form of <paramref name="plaintext"/>.</summary>
    /// <param name="plaintext">The credential. Never log it.</param>
    /// <returns>The protected value, safe to store.</returns>
    string Protect(string plaintext);

    /// <summary>Returns the plaintext of a value that <see cref="Protect"/> returned.</summary>
    /// <param name="protectedValue">A value returned by <see cref="Protect"/>.</param>
    /// <returns>The original credential.</returns>
    string Unprotect(string protectedValue);
}
