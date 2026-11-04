using Ambinder.A2A.Client;
using Ambinder.A2A.Client.Auth;
using Ambinder.A2A.Client.Grpc;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Interop.Tests.Agent;

/// <summary>The three bindings of the matrix, with the card names the client factory matches on.</summary>
public static class Bindings
{
    public static readonly string[] All = [A2AConstants.BindingJsonRpc, A2AConstants.BindingRest, A2AConstants.BindingGrpc];

    public static TheoryData<string> Theory() => [.. All];
}

/// <summary>
/// The ONE place the interop tests create our client. Today it uses the interim <c>A2AClientFactory</c>; package 10 replaces the
/// body with <c>AddA2AClient</c> / <c>IA2AClientFactory</c> and nothing else in the test project changes.
/// </summary>
public static class TestClients
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Fetches the card at <paramref name="cardUrl"/> (no auth).</summary>
    public static Task<AgentCard> GetCardAsync(string cardUrl, CancellationToken ct)
        => new A2ACardResolver(Http).GetAgentCardAsync(new Uri(cardUrl), ct);

    /// <summary>A client for the agent behind <paramref name="cardUrl"/> over <paramref name="binding"/>.</summary>
    /// <param name="cardUrl">Card URL of the agent.</param>
    /// <param name="binding"><c>JSONRPC</c>, <c>HTTP+JSON</c> or <c>GRPC</c>.</param>
    /// <param name="bearerToken">Sent as <c>Authorization: Bearer</c> on every call, or null for anonymous calls.</param>
    /// <param name="extensions">Extension URIs for <c>A2A-Extensions</c>.</param>
    /// <param name="ct">Cancels the card fetch.</param>
    public static async Task<IA2AClient> CreateAsync(string cardUrl, string binding, string? bearerToken = null,
        IReadOnlyList<string>? extensions = null, CancellationToken ct = default)
    {
        var card = await GetCardAsync(cardUrl, ct);
        return A2AClientFactory.Create(card, new A2AClientOptions
        {
            HttpClient = Http,
            Credentials = bearerToken is null ? null : new FixedBearer(bearerToken),
            PreferredBinding = binding,
            Transports = [GrpcClientTransport.Instance],
            Extensions = extensions ?? [],
        });
    }

    private sealed class FixedBearer(string token) : IA2ACredentialProvider
    {
        public ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(AgentCard card, CancellationToken ct)
            => new(new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });
    }
}
