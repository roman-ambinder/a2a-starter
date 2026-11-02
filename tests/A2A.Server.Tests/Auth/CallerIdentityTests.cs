using System.Security.Claims;
using A2A.Protocol;
using A2A.Server.Auth;

namespace A2A.Server.Tests.Auth;

public class CallerIdentityTests
{
    private static ClaimsPrincipal User(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Fact]
    public void Unauthenticated_is_anonymous()
        => Assert.Equal("anonymous", CallerIdentity.GetOwner(new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", "x")]))));

    [Fact]
    public void Bearer_is_tenant_scoped()
    {
        Assert.Equal("tenant:t1", CallerIdentity.GetOwner(User(("tid", "t1"), ("sub", "app-1"))));
        Assert.Equal("tenant:t2", CallerIdentity.GetOwner(User((CallerIdentity.MappedTenantIdClaim, "t2"))));
    }

    [Fact]
    public void Api_key_uses_key_name()
        => Assert.Equal("apikey:dev", CallerIdentity.GetOwner(User((CallerIdentity.ApiKeyClaim, "dev"))));

    [Fact]
    public void Authenticated_without_tenant_or_key_uses_subject()
        => Assert.Equal("user:s1", CallerIdentity.GetOwner(User(("sub", "s1"))));

    [Fact]
    public void Authenticated_without_any_identifier_is_rejected()
    {
        var ex = Assert.Throws<A2AAuthException>(() => CallerIdentity.GetOwner(User(("role", "x"))));
        Assert.Equal(401, ex.HttpStatus);
    }
}
