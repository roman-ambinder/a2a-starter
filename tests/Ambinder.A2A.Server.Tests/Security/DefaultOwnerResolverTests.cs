using System.Security.Claims;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Security;

namespace Ambinder.A2A.Server.Tests.Security;

public class DefaultOwnerResolverTests
{
    private const string MappedTenant = "http://schemas.microsoft.com/identity/claims/tenantid";
    private const string MappedObjectId = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string MappedScope = "http://schemas.microsoft.com/identity/claims/scope";

    private static readonly DefaultOwnerResolver Resolver = new();
    private static readonly AgentRegistration Agent = new("a", typeof(object), new AgentCard { Name = "a" }, null, null);

    private static ClaimsPrincipal User(string? authenticationType, params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), authenticationType));

    private static ValueTask<A2ACaller> Resolve(ClaimsPrincipal user) => Resolver.ResolveAsync(user, Agent, CancellationToken.None);

    [Fact]
    public async Task Unauthenticated_is_anonymous()
    {
        var caller = await Resolve(User(null, ("tid", "x")));
        Assert.Equal(CallerKind.Anonymous, caller.Kind);
        Assert.Equal("anonymous", caller.Owner);
    }

    [Fact]
    public async Task App_only_token_is_owned_by_tenant_and_app()
    {
        var caller = await Resolve(User("test", ("tid", "t1"), ("azp", "a1")));
        Assert.Equal(CallerKind.Application, caller.Kind);
        Assert.Equal("tid:t1/app:a1", caller.Owner);
        Assert.Equal("a1", caller.AppId);
        Assert.Equal("t1", caller.EntraTenantId);
        Assert.Null(caller.UserObjectId);
    }

    [Fact]
    public async Task App_only_v1_token_uses_appid()
    {
        var caller = await Resolve(User("test", ("tid", "t1"), ("appid", "a1")));
        Assert.Equal(CallerKind.Application, caller.Kind);
        Assert.Equal("tid:t1/app:a1", caller.Owner);
        Assert.Equal("a1", caller.AppId);
    }

    [Fact]
    public async Task App_only_token_without_app_id_is_401()
    {
        var ex = await Assert.ThrowsAsync<A2AAuthException>(async () => await Resolve(User("test", ("tid", "t1"))));
        Assert.Equal(401, ex.HttpStatus);
        Assert.Equal("App-only token has no azp or appid claim.", ex.Message);
    }

    [Fact]
    public async Task Delegated_token_is_owned_by_tenant_and_user()
    {
        var caller = await Resolve(User("test", ("tid", "t1"), ("scp", "A2A.Invoke"), ("oid", "u1"), ("azp", "a1")));
        Assert.Equal(CallerKind.User, caller.Kind);
        Assert.Equal("tid:t1/user:u1", caller.Owner);
        Assert.Equal("a1", caller.AppId);
        Assert.Equal("u1", caller.UserObjectId);
        Assert.Equal("t1", caller.EntraTenantId);
    }

    [Fact]
    public async Task Two_users_of_one_app_get_different_owners()
    {
        var one = await Resolve(User("test", ("tid", "t1"), ("scp", "s"), ("oid", "u1"), ("azp", "a1")));
        var two = await Resolve(User("test", ("tid", "t1"), ("scp", "s"), ("oid", "u2"), ("azp", "a1")));
        Assert.NotEqual(one.Owner, two.Owner);
    }

    [Fact]
    public async Task Delegated_token_without_oid_is_401()
    {
        var ex = await Assert.ThrowsAsync<A2AAuthException>(async () => await Resolve(User("test", ("tid", "t1"), ("scp", "s"), ("azp", "a1"))));
        Assert.Equal(401, ex.HttpStatus);
        Assert.Equal("Delegated token has no oid claim.", ex.Message);
    }

    [Fact]
    public async Task Unknown_scheme_without_tenant_is_401()
    {
        var ex = await Assert.ThrowsAsync<A2AAuthException>(async () => await Resolve(User("ApiKey", ("a2a:apikey", "ops"))));
        Assert.Equal(401, ex.HttpStatus);
        Assert.Equal(
            "The caller was authenticated by scheme 'ApiKey', which the default owner resolver does not understand. "
            + "Register an IOwnerResolver for this scheme (see docs: Security > Other schemes).",
            ex.Message);
    }

    [Fact]
    public async Task Mapped_long_claim_names_are_accepted()
    {
        var app = await Resolve(User("test", (MappedTenant, "t1"), ("azp", "a1")));
        Assert.Equal("tid:t1/app:a1", app.Owner);

        var user = await Resolve(User("test", (MappedTenant, "t1"), (MappedScope, "s"), (MappedObjectId, "u1"), ("azp", "a1")));
        Assert.Equal(CallerKind.User, user.Kind);
        Assert.Equal("tid:t1/user:u1", user.Owner);
    }

    [Fact]
    public async Task Principal_is_a_snapshot()
    {
        var principal = User("test", ("tid", "t1"), ("azp", "a1"));
        var caller = await Resolve(principal);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("late", "x"));
        Assert.Null(caller.Principal.FindFirst("late"));
        Assert.Equal("a1", caller.Principal.FindFirst("azp")?.Value);
    }

    [Fact]
    public async Task Resolver_never_sets_the_access_token()
    {
        var caller = await Resolve(User("test", ("tid", "t1"), ("azp", "a1")));
        Assert.Null(caller.AccessToken);
    }

    [Fact]
    public void ToString_hides_the_access_token()
    {
        var principal = User("test", ("tid", "t1"), ("azp", "a1"));
        var withToken = new A2ACaller { Owner = "o", Kind = CallerKind.Other, Principal = principal, AccessToken = "s3cr3t-token" };
        var text = withToken.ToString();
        Assert.DoesNotContain("s3cr3t-token", text);
        Assert.Contains("AccessToken = ***", text);

        var without = new A2ACaller { Owner = "o", Kind = CallerKind.Other, Principal = principal };
        Assert.Contains("AccessToken = null", without.ToString());
    }
}
