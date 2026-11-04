using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Ambinder.A2A.Shared;

/// <summary>
/// B13: the strong ETag of an agent card. Linked as one source file into the projects that need it, compiled <c>internal</c> in each.
/// </summary>
internal static class AgentCardETag
{
    /// <summary>Strong ETag over the unsigned card payload plus the sorted signing key ids.</summary>
    /// <param name="payload">The card JSON bytes without the <c>signatures</c> member.</param>
    /// <param name="keyIds">The <c>kid</c> of each signature; ordered ordinally before hashing. Empty for an unsigned card.</param>
    /// <returns><c>"</c> + base64url of the first 16 bytes of SHA-256 + <c>"</c>.</returns>
    public static string Compute(ReadOnlySpan<byte> payload, IEnumerable<string> keyIds)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(payload);
        foreach (var kid in keyIds.Order(StringComparer.Ordinal))
        {
            hash.AppendData("\n"u8);
            hash.AppendData(Encoding.UTF8.GetBytes(kid));
        }
        Span<byte> digest = stackalloc byte[32];
        hash.GetHashAndReset(digest);
        return $"\"{Base64Url.EncodeToString(digest[..16])}\"";
    }
}
