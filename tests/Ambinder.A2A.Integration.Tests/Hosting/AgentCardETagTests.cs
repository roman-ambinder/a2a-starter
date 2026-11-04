using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.AspNetCore.Hosting;
using Ambinder.A2A.Server.Hosting;
using Ambinder.A2A.Shared;

namespace Ambinder.A2A.Integration.Tests.Hosting;

/// <summary>B13: the card ETag covers the unsigned card and the signing key ids, nothing else.</summary>
public class AgentCardETagTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("""{"name":"echo"}""");

    [Fact]
    public void Unsigned_etag_is_the_first_16_bytes_of_the_sha256()
    {
        var expected = $"\"{Base64Url.EncodeToString(SHA256.HashData(Payload).AsSpan(0, 16))}\"";

        Assert.Equal(expected, AgentCardETag.Compute(Payload, []));
        Assert.StartsWith("\"", expected);
    }

    [Fact]
    public void Key_ids_are_appended_after_a_newline_in_ordinal_order()
    {
        var input = Payload.Concat("\na"u8.ToArray()).Concat("\nb"u8.ToArray()).ToArray();
        var expected = $"\"{Base64Url.EncodeToString(SHA256.HashData(input).AsSpan(0, 16))}\"";

        Assert.Equal(expected, AgentCardETag.Compute(Payload, ["b", "a"]));
        Assert.Equal(AgentCardETag.Compute(Payload, ["a", "b"]), AgentCardETag.Compute(Payload, ["b", "a"]));
        Assert.NotEqual(AgentCardETag.Compute(Payload, ["a"]), AgentCardETag.Compute(Payload, ["a", "b"]));
    }

    [Fact]
    public void Two_renderers_give_the_same_etag_for_the_same_card()
    {
        var card = Card();

        Assert.Equal(Render(card).ETag, Render(card).ETag);
        Assert.Equal(Render(card).Bytes, Render(card).Bytes);
    }

    [Fact]
    public void Re_signing_with_the_same_key_keeps_the_etag()
    {
        var first = Render(Card(Signature("k1", "sig-a")));
        var second = Render(Card(Signature("k1", "sig-b")));

        Assert.Equal(first.ETag, second.ETag);
        Assert.NotEqual(first.Bytes, second.Bytes);
    }

    [Fact]
    public void A_different_signing_key_changes_the_etag()
        => Assert.NotEqual(Render(Card(Signature("k1", "sig"))).ETag, Render(Card(Signature("k2", "sig"))).ETag);

    [Fact]
    public void A_signed_card_differs_from_an_unsigned_one_only_by_the_key_ids()
    {
        var unsigned = Card();
        var signed = Card(Signature("k1", "sig"));

        var expected = AgentCardETag.Compute(Render(unsigned).Bytes, ["k1"]);

        Assert.Equal(expected, Render(signed).ETag);
    }

    [Fact]
    public void The_key_id_is_read_from_the_unprotected_header_when_the_protected_one_has_none()
    {
        var protectedOnly = Card(new AgentCardSignature { Protected = Base64Url.EncodeToString("""{"alg":"ES256"}"""u8), Signature = "s",
            Header = System.Text.Json.JsonDocument.Parse("""{"kid":"k1"}""").RootElement.Clone() });

        Assert.Equal(Render(Card(Signature("k1", "s"))).ETag, Render(protectedOnly).ETag);
    }

    private static RenderedCard Render(AgentCard card) => new AgentCardRenderer([]).Render("echo", card);

    private static AgentCard Card(params AgentCardSignature[] signatures) => new() { Name = "echo", Description = "d", Version = "1", Signatures = signatures };

    private static AgentCardSignature Signature(string kid, string value) => new()
    {
        Protected = Base64Url.EncodeToString(Encoding.UTF8.GetBytes($$"""{"alg":"ES256","kid":"{{kid}}"}""")),
        Signature = value,
    };
}
