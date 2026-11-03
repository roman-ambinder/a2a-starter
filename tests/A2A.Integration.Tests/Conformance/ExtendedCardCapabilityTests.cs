using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;

namespace A2A.Integration.Tests.Conformance;

/// <summary>
/// Spec 3.3.4 (TCK CORE-CAP-003): when the card does not declare <c>capabilities.extendedAgentCard</c>, GetExtendedAgentCard
/// returns UnsupportedOperation, also to an anonymous caller (the operation does not exist, so there is nothing to authenticate for).
/// </summary>
[Collection(A2ATestHostCollection.Name)]
public sealed class ExtendedCardCapabilityTests(A2ATestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory, MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Extended_card_not_declared_is_unsupported_for_anonymous_callers(string binding)
    {
        var card = await host.GetCardAsync("echo", Ct);
        Assert.NotEqual(true, card.Capabilities.ExtendedAgentCard);
        await using var client = await host.CreateClientAsync("echo", binding, ct: Ct);

        var ex = await Assert.ThrowsAsync<A2AException>(() => client.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest(), Ct));

        Assert.Equal(A2AErrorCode.UnsupportedOperation, ex.Code);
    }
}
