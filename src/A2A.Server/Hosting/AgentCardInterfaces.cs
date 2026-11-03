using A2A.Protocol;

namespace A2A.Server.Hosting;

/// <summary>Builds the <c>supportedInterfaces</c> list of a hosted agent's card from <see cref="A2AServerOptions"/>.</summary>
public static class AgentCardInterfaces
{
    /// <summary>Returns <paramref name="card"/> with its interfaces replaced by <see cref="For"/>.</summary>
    public static AgentCard Apply(AgentCard card, string agentName, A2AServerOptions options)
        => card with { SupportedInterfaces = For(agentName, options) };

    /// <summary>
    /// JSON-RPC <c>{base}/agents/{name}/jsonrpc</c>, REST <c>{base}/agents/{name}/rest</c>, and, when a port is configured, gRPC
    /// <c>{host}:{port}</c> (the gRPC address form the spec gives for <c>AgentInterface.url</c>; gRPC clients use it as the channel
    /// target). Whether the gRPC listener uses TLS follows the scheme of PublicBaseUrl.
    /// </summary>
    public static IReadOnlyList<AgentInterface> For(string agentName, A2AServerOptions options)
    {
        var baseUrl = options.BaseUrl;
        var list = new List<AgentInterface>
        {
            new() { Url = $"{baseUrl}/agents/{agentName}/jsonrpc", ProtocolBinding = A2AConstants.BindingJsonRpc, ProtocolVersion = A2AConstants.ProtocolVersion },
            new() { Url = $"{baseUrl}/agents/{agentName}/rest", ProtocolBinding = A2AConstants.BindingRest, ProtocolVersion = A2AConstants.ProtocolVersion },
        };
        if (options.GrpcPorts.TryGetValue(agentName, out var port))
        {
            var uri = new Uri(baseUrl);
            var host = uri.Host; // IPv6 literals keep their brackets
            list.Add(new() { Url = $"{host}:{port}", ProtocolBinding = A2AConstants.BindingGrpc, ProtocolVersion = A2AConstants.ProtocolVersion });
        }
        return list;
    }
}
