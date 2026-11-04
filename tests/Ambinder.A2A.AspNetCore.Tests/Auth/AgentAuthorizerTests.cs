using System.Security.Claims;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.AspNetCore.Auth;
using Ambinder.A2A.Server.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.AspNetCore.Tests.Auth;

public class AgentAuthorizerTests
{
    private static ClaimsPrincipal SecondaryOnly() => new(
    [
        new ClaimsIdentity(),                                          // primary: not authenticated
        new ClaimsIdentity([new Claim("sub", "s1")], "secondary"),     // secondary: authenticated
    ]);

    private static A2ACaller OwnerOf(ClaimsPrincipal user)
        => new DefaultOwnerResolver().ResolveAsync(user, Agent(null), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    private static AgentRegistration Agent(string? policy)
        => new("a", typeof(object), new AgentCard { Name = "a" }, null, policy);

    private static AgentAuthorizer Authorizer()
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization(o =>
            o.AddPolicy("Any", p => p.RequireAssertion(_ => true))).BuildServiceProvider();
        return new AgentAuthorizer(services.GetRequiredService<IAuthorizationService>());
    }

    [Fact]
    public void Authenticated_rule_is_the_primary_identity_like_the_owner_resolver()
    {
        var user = SecondaryOnly();
        Assert.False(AgentAuthorizer.IsAuthenticated(user));
        Assert.Equal(CallerKind.Anonymous, OwnerOf(user).Kind);

        var primary = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", "t1"), new Claim("azp", "a1")], "primary"));
        Assert.True(AgentAuthorizer.IsAuthenticated(primary));
        Assert.Equal("tid:t1/app:a1", OwnerOf(primary).Owner);
    }

    [Fact]
    public async Task Policy_agent_rejects_principal_with_only_a_secondary_identity()
    {
        var ex = await Assert.ThrowsAsync<A2AAuthException>(() =>
            Authorizer().AuthorizeAsync(Agent("Any"), SecondaryOnly(), TestContext.Current.CancellationToken));
        Assert.Equal(401, ex.HttpStatus);
    }
}
