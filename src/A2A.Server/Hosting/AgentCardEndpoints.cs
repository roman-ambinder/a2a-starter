using System.Text.Json;
using A2A.Protocol;
using A2A.Server.Agents;
using A2A.Server.Push;
using A2A.Server.Transports;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace A2A.Server.Hosting;

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
        http.Response.ContentType = "application/json";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(agent.PublicCard, A2AJson.Options.GetTypeInfo(typeof(AgentCard)));
        await http.Response.Body.WriteAsync(bytes, http.RequestAborted);
    }
}
