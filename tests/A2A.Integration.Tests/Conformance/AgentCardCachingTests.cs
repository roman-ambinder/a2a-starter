using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;
using A2A.Server.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace A2A.Integration.Tests.Conformance;

/// <summary>
/// Spec 8.6.1 / design D7: agent card responses carry Cache-Control max-age and a strong ETag, and answer If-None-Match with 304;
/// the extended card is never stored; registered <see cref="IAgentCardContributor"/>s shape the served public card and its ETag (not the extended card).
/// </summary>
[Collection(A2ATestHostCollection.Name)]
public sealed class AgentCardCachingTests(A2ATestHost host)
{
    /// <summary>Port base of the extra AgentServers these tests start, one at a time.</summary>
    private static readonly int ExtraPortBase = A2ATestHost.PortBase + 6000;

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

    [Theory]
    [InlineData("rest")]
    [InlineData("jsonrpc")]
    public async Task Extended_card_is_no_store(string transport)
    {
        using var request = ExtendedCardRequest(A2ATestHost.BaseUrl, transport, await host.GetTokenAsync(TestAuth.Client, Ct));

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.True(response.Headers.CacheControl.Private);
    }

    [Fact]
    public async Task Contributor_member_appears_and_changes_etag()
    {
        var (plainCard, plainEtag) = await FetchPublicCardAsync();
        var (card, etag) = await FetchPublicCardAsync(new Contributor((_, json) => json["x-test"] = 1));

        Assert.False(plainCard.ContainsKey("x-test"));
        Assert.Equal(1, card["x-test"]!.GetValue<int>());
        Assert.NotEqual(plainEtag, etag);
    }

    [Fact]
    public async Task Contributors_run_in_registration_order()
    {
        var (card, _) = await FetchPublicCardAsync(
            new Contributor((_, json) => json["x-first"] = 1),
            new Contributor((_, json) => json["x-second"] = json["x-first"]!.GetValue<int>() + 1));

        Assert.Equal(2, card["x-second"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("rest")]
    [InlineData("jsonrpc")]
    public async Task Contributors_do_not_run_for_the_extended_card(string transport)
    {
        var contributor = new Contributor((_, json) => json["x-test"] = 1);
        await using var server = await StartExtraServerAsync(contributor);
        using var request = ExtendedCardRequest(A2ATestHost.AgentServerBaseUrl(ExtraPortBase), transport,
            await host.GetTokenAsync(TestAuth.Client, Ct));

        using var response = await host.Http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!.AsObject();
        var card = transport == "jsonrpc" ? body["result"]!.AsObject() : body;
        Assert.False(card.ContainsKey("x-test"));
    }

    [Fact]
    public async Task Etag_is_stable_across_two_hosts()
    {
        // Two hosts from the same configuration. The card holds the host's own URLs, so both use the same port base, one after the other.
        var (_, first) = await FetchPublicCardAsync();
        var (_, second) = await FetchPublicCardAsync();

        Assert.Equal(first, second);
    }

    private static HttpRequestMessage ExtendedCardRequest(string baseUrl, string transport, string token)
    {
        var request = transport == "jsonrpc"
            ? new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/agents/report/jsonrpc")
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"GetExtendedAgentCard","params":{}}""",
                    Encoding.UTF8, A2AConstants.ContentType),
            }
            : new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/agents/report/rest/extendedAgentCard");
        request.Headers.Add(A2AConstants.VersionHeader, "1.0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>Starts an extra AgentServer with <paramref name="contributors"/>, reads the echo card and its ETag, and stops it.</summary>
    private async Task<(JsonObject Card, EntityTagHeaderValue ETag)> FetchPublicCardAsync(params IAgentCardContributor[] contributors)
    {
        await using var server = await StartExtraServerAsync(contributors);
        using var response = await host.Http.GetAsync(
            $"{A2ATestHost.AgentServerBaseUrl(ExtraPortBase)}/agents/echo/.well-known/agent-card.json", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var card = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!.AsObject();
        return (card, response.Headers.ETag!);
    }

    private static Task<WebApplication> StartExtraServerAsync(params IAgentCardContributor[] contributors)
        => A2ATestHost.StartAgentServerAsync(ExtraPortBase, A2ATestHost.MockEntraBaseUrl, b => b.Host.ConfigureServices((_, services) =>
        {
            foreach (var contributor in contributors) services.AddSingleton<IAgentCardContributor>(contributor);
        }));

    private sealed class Contributor(Action<AgentCardContext, JsonObject> contribute) : IAgentCardContributor
    {
        public void Contribute(AgentCardContext card, JsonObject cardJson) => contribute(card, cardJson);
    }
}
