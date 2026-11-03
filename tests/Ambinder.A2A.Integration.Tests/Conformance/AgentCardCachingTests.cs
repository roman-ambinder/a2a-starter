using System.Net;
using System.Net.Http.Headers;
using Ambinder.A2A.Integration.Tests.Fixtures;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Integration.Tests.Conformance;

/// <summary>Spec 8.6.1: agent card responses carry Cache-Control max-age and a strong ETag, and answer If-None-Match with 304.</summary>
[Collection(A2ATestHostCollection.Name)]
public sealed class AgentCardCachingTests(A2ATestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/.well-known/agent-card.json")]
    [InlineData("/agents/echo/.well-known/agent-card.json")]
    public async Task Card_has_cache_headers_and_supports_conditional_get(string path)
    {
        using var first = await host.Http.GetAsync(A2ATestHost.BaseUrl + path, Ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);
        Assert.False(etag.IsWeak);
        Assert.Equal(TimeSpan.FromMinutes(5), first.Headers.CacheControl!.MaxAge);
        Assert.True(first.Headers.CacheControl.Public);
        Assert.Contains(A2AConstants.VersionHeader, first.Headers.Vary);

        // Same bytes → same ETag on the next request.
        using var again = await host.Http.GetAsync(A2ATestHost.BaseUrl + path, Ct);
        Assert.Equal(etag, again.Headers.ETag);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, A2ATestHost.BaseUrl + path);
        conditional.Headers.IfNoneMatch.Add(etag);
        using var notModified = await host.Http.SendAsync(conditional, Ct);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Empty(await notModified.Content.ReadAsByteArrayAsync(Ct));

        using var stale = new HttpRequestMessage(HttpMethod.Get, A2ATestHost.BaseUrl + path);
        stale.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"other\""));
        using var changed = await host.Http.SendAsync(stale, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
    }

    [Fact]
    public async Task Different_cards_have_different_etags()
    {
        using var echo = await host.Http.GetAsync($"{A2ATestHost.BaseUrl}/agents/echo/.well-known/agent-card.json", Ct);
        using var report = await host.Http.GetAsync($"{A2ATestHost.BaseUrl}/agents/report/.well-known/agent-card.json", Ct);
        Assert.NotEqual(echo.Headers.ETag, report.Headers.ETag);
    }

    [Fact]
    public async Task Extended_card_is_not_stored_by_caches()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{A2ATestHost.BaseUrl}/agents/report/rest/extendedAgentCard");
        request.Headers.Add(A2AConstants.VersionHeader, "1.0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await host.GetTokenAsync(TestAuth.Client, Ct));

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.True(response.Headers.CacheControl.Private);
    }
}
