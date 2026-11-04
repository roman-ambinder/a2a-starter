using Ambinder.A2A.AspNetCore.Transports;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Hosting;
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
        var jwks = http.RequestServices.GetRequiredKeyedService<Func<System.Text.Json.Nodes.JsonObject>>(InternalServiceKeys.PushJwks)();
        http.Response.ContentType = "application/json";
        await http.Response.WriteAsync(jwks.ToJsonString(), http.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteCardAsync(HttpContext http, string? agentName)
    {
        var catalog = http.RequestServices.GetRequiredService<AgentCatalog>();
        if (agentName is null || !catalog.TryGet(agentName, out var agent))
        {
            await TransportErrors.WriteUnknownAgentAsync(http, agentName).ConfigureAwait(false);
            return;
        }
        var options = http.RequestServices.GetRequiredService<IOptions<A2AServerOptions>>().Value;
        var (bytes, etag) = http.RequestServices.GetRequiredService<AgentCardRenderer>().Render(agent.Name, agent.PublicCard);

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
        await http.Response.Body.WriteAsync(bytes, http.RequestAborted).ConfigureAwait(false);
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
