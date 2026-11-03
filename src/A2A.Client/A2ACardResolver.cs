using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using A2A.Protocol;

namespace A2A.Client;

/// <summary>Fetches agent cards. Results are cached per resolved URL for 5 minutes.</summary>
public sealed class A2ACardResolver(HttpClient httpClient, TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Uri, (AgentCard Card, DateTimeOffset Expires)> _cache = new();

    /// <summary>
    /// Gets the card. A URL whose path ends in <c>.json</c> is used as is; otherwise
    /// <c>/.well-known/agent-card.json</c> is appended to it.
    /// </summary>
    public async Task<AgentCard> GetAgentCardAsync(Uri baseOrCardUrl, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(baseOrCardUrl);
        var url = Resolve(baseOrCardUrl);

        if (_cache.TryGetValue(url, out var hit) && _time.GetUtcNow() < hit.Expires) return hit.Card;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new A2AAuthException((int)response.StatusCode, $"Fetching the agent card from {url} was refused (HTTP {(int)response.StatusCode}).");
        if (!response.IsSuccessStatusCode)
            throw new A2AException(A2AErrorCode.InternalError, $"Fetching the agent card from {url} failed with HTTP {(int)response.StatusCode}. Check the URL and that the agent is running.");

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        AgentCard? card;
        try
        {
            card = JsonSerializer.Deserialize(bytes, A2AJsonContext.Default.AgentCard);
        }
        catch (JsonException ex)
        {
            throw new A2AException(A2AErrorCode.InvalidAgentResponse, $"The agent card at {url} is not valid: {ex.Message} Check that the URL serves an A2A agent card.");
        }
        if (card is null)
            throw new A2AException(A2AErrorCode.InvalidAgentResponse, $"The agent card at {url} is empty. Check that the URL serves an A2A agent card.");

        _cache[url] = (card, _time.GetUtcNow() + CacheDuration);
        return card;
    }

    private static Uri Resolve(Uri url)
    {
        if (url.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return url;
        var builder = new UriBuilder(url) { Path = url.AbsolutePath.TrimEnd('/') + A2AConstants.WellKnownCardPath };
        return builder.Uri;
    }
}
