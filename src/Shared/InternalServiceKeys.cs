namespace Ambinder.A2A;

/// <summary>
/// Keys for services that one shipped package registers and another resolves, where the service type itself is internal.
/// Interim seam: package 08 replaces the push key with a public key-provider interface.
/// </summary>
internal static class InternalServiceKeys
{
    /// <summary>Keyed <c>Func&lt;System.Text.Json.Nodes.JsonObject&gt;</c> that returns the push signature JWKS.</summary>
    public const string PushJwks = "a2a.push.jwks";
}
