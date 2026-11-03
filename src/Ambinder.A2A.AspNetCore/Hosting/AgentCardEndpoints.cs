using Ambinder.A2A.Server.Hosting;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Push;
using Ambinder.A2A.AspNetCore.Transports;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Ambinder.A2A.AspNetCore.Hosting;

/// <summary>Anonymous discovery endpoints: agent cards and the push signature JWKS.</summary>
internal static class AgentCardEndpoints
{
    public static Task RootCardAsync(HttpContext http)
    {
        var options = http.RequestServices.GetRequiredService<IOptions<A2AServerOptions>>().Value;
        return WriteCardAsync(http, options.DefaultAgent);
    }

    public static Task AgentCardAsync(HttpContext http) => WriteCardAsync(http, http.Request.RouteValues["agent"] as string);

    public static async Task PushJwksAsync(HttpContext http)
    {
        var jwks = http.RequestServices.GetRequiredService<PushSigningKey>().GetJwks();
        http.Response.ContentType = "application/json";
        await http.Response.WriteAsync(jwks.ToJsonString(), http.RequestAborted);
    }

    private static async Task WriteCardAsync(HttpContext http, string? agentName)
    {
        var catalog = http.RequestServices.GetRequiredService<AgentCatalog>();
        if (agentName is null || !catalog.TryGet(agentName, out var agent))
        {
            await TransportErrors.WriteUnknownAgentAsync(http, agentName);
            return;
        }
        var options = http.RequestServices.GetRequiredService<IOptions<A2AServerOptions>>().Value;
        var (bytes, etag) = CardBytes.GetValue(agent.PublicCard, Serialize);

        // Spec 8.6.1: Cache-Control max-age and an ETag; conditional GET answers 304. Vary, so a shared cache never serves
        // one protocol version's card to a client of another.
        var headers = http.Response.Headers;
        headers.ETag = etag;
        headers.CacheControl = options.CardCacheMaxAge > TimeSpan.Zero
            ? $"public, max-age={(long)options.CardCacheMaxAge.TotalSeconds}"
            : "no-cache";
        headers.Vary = A2AConstants.VersionHeader;
        if (IfNoneMatch(http.Request.Headers.IfNoneMatch, etag))
        {
            http.Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        http.Response.ContentType = "application/json";
        await http.Response.Body.WriteAsync(bytes, http.RequestAborted);
    }

    // Serialized once per card instance (cards are immutable records built at startup).
    private static readonly ConditionalWeakTable<AgentCard, CachedCard> CardBytes = new();

    private sealed record CachedCard(byte[] Bytes, string ETag);

    /// <summary>Card JSON plus a strong ETag: base64url of the first 16 bytes of its SHA-256, so every instance agrees.</summary>
    private static CachedCard Serialize(AgentCard card)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(card, A2AJson.Options.GetTypeInfo(typeof(AgentCard)));
        var hash = SHA256.HashData(bytes);
        return new CachedCard(bytes, $"\"{Base64Url.EncodeToString(hash.AsSpan(0, 16))}\"");
    }

    /// <summary>True when an <c>If-None-Match</c> value lists <paramref name="etag"/> (weak comparison, RFC 9110 13.1.2) or is <c>*</c>.</summary>
    private static bool IfNoneMatch(StringValues values, string etag)
    {
        foreach (var value in values)
        {
            foreach (var candidate in (value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (candidate == "*") return true;
                var tag = candidate.StartsWith("W/", StringComparison.Ordinal) ? candidate[2..] : candidate;
                if (tag == etag) return true;
            }
        }
        return false;
    }
}
