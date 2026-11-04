using System.Text.Json;
using Ambinder.A2A.Protocol.ProtoMapping;

namespace Ambinder.A2A.Protocol.Tests.Grpc;

/// <summary>
/// Design D10: the deprecated <c>implicit</c> and <c>password</c> OAuth flows survive a card round trip, so a card that
/// declares them is passed on unchanged. JSON to JSON, proto to proto, and JSON to proto to JSON.
/// </summary>
public class OAuthFlowRoundTripTests
{
    public static TheoryData<string> Flows => ["implicit", "password"];

    private static AgentCard CardWith(string flow)
        => Samples.FullCard(flow == "implicit" ? Samples.ImplicitFlows() : Samples.PasswordFlows());

    private static string Ours(AgentCard card) => JsonSerializer.Serialize(card, A2AJson.Options);

#pragma warning disable CS0618 // deprecated OAuth flows: read here to test that they round-trip
    private static void AssertFlow(string flow, AgentCard card)
    {
        var flows = card.SecuritySchemes!["oauth"].Oauth2SecurityScheme!.Flows;
        if (flow == "implicit")
        {
            Assert.Equal("https://idp.example/authorize", flows.Implicit!.AuthorizationUrl);
            Assert.Equal("https://idp.example/refresh", flows.Implicit.RefreshUrl);
            Assert.Equal(Samples.ImplicitFlows().Implicit!.Scopes, flows.Implicit.Scopes);
            Assert.Null(flows.Password);
        }
        else
        {
            Assert.Equal("https://idp.example/token", flows.Password!.TokenUrl);
            Assert.Equal("https://idp.example/refresh", flows.Password.RefreshUrl);
            Assert.Equal(Samples.PasswordFlows().Password!.Scopes, flows.Password.Scopes);
            Assert.Null(flows.Implicit);
        }
    }
    [Fact]
    public void Deprecated_flows_are_marked_obsolete_with_the_spec_text()
    {
        const string text = "Removed from OAuth 2.1; kept so cards round-trip. See docs/spec-decisions.md.";
        foreach (var member in new System.Reflection.MemberInfo[]
                 {
                     typeof(ImplicitOAuthFlow), typeof(PasswordOAuthFlow),
                     typeof(OAuthFlows).GetProperty(nameof(OAuthFlows.Implicit))!, typeof(OAuthFlows).GetProperty(nameof(OAuthFlows.Password))!,
                 })
            Assert.Equal(text, Assert.Single(member.GetCustomAttributes(typeof(ObsoleteAttribute), false).Cast<ObsoleteAttribute>()).Message);
    }
#pragma warning restore CS0618

    [Theory, MemberData(nameof(Flows))]
    public void Json_in_json_out(string flow)
    {
        var json = Ours(CardWith(flow));
        Assert.Contains($"\"{flow}\":", json);

        var back = JsonSerializer.Deserialize<AgentCard>(json, A2AJson.Options)!;

        AssertFlow(flow, back);
        Assert.Equal(json, Ours(back));
    }

    [Theory, MemberData(nameof(Flows))]
    public void Proto_in_proto_out(string flow)
    {
        var proto = (Lf.A2a.V1.AgentCard)ProtoMapper.ToProtoObject(CardWith(flow));

        var back = ProtoMapper.FromProtoObject(proto);
        var again = (Lf.A2a.V1.AgentCard)ProtoMapper.ToProtoObject(back);

        AssertFlow(flow, (AgentCard)back);
        Assert.Equal(proto, again);
    }

    [Theory, MemberData(nameof(Flows))]
    public void Json_to_proto_to_json(string flow)
    {
        var json = Ours(CardWith(flow));
        var fromJson = JsonSerializer.Deserialize<AgentCard>(json, A2AJson.Options)!;

        var viaProto = (AgentCard)ProtoMapper.FromProtoObject(ProtoMapper.ToProtoObject(fromJson));

        AssertFlow(flow, viaProto);
        Assert.Equal(json, Ours(viaProto));
    }
}
