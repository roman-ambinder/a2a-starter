using Ambinder.A2A.Server.Transports.JsonRpc;
using Ambinder.A2A.Server.Transports.Rest;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ambinder.A2A.Server.Hosting;

public static class A2AEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps <c>GET /.well-known/agent-card.json</c> (default agent), <c>GET /agents/{agent}/.well-known/agent-card.json</c>,
    /// <c>GET /.well-known/a2a-push-jwks.json</c> <c>POST /agents/{agent}/jsonrpc</c> and the REST binding under <c>/agents/{agent}/rest</c>.
    /// Cards and the JWKS are anonymous and need no <c>A2A-Version</c>. Returns the group so callers can add conventions.
    /// </summary>
    public static RouteGroupBuilder MapA2A(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup("");
        group.MapGet("/.well-known/agent-card.json", (RequestDelegate)AgentCardEndpoints.RootCardAsync);
        group.MapGet("/agents/{agent}/.well-known/agent-card.json", (RequestDelegate)AgentCardEndpoints.AgentCardAsync);
        group.MapGet("/.well-known/a2a-push-jwks.json", (RequestDelegate)AgentCardEndpoints.PushJwksAsync);
        group.MapPost("/agents/{agent}/jsonrpc", (RequestDelegate)JsonRpcEndpoint.HandleAsync);
        group.MapMethods("/agents/{agent}/rest/{**path}", ["GET", "POST", "DELETE"], (RequestDelegate)RestEndpoints.HandleAsync);
        return group;
    }
}
