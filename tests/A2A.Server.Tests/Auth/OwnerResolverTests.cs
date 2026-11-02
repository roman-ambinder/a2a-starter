using System.Security.Claims;
using A2A.Protocol;
using A2A.Server.Auth;

namespace A2A.Server.Tests.Auth;

public class OwnerResolverTests
{
    private static readonly DefaultOwnerResolver Resolver = DefaultOwnerResolver.Instance;

    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) => UserOf("Bearer", claims);

    private static ClaimsPrincipal UserOf(string authenticationType, params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), authenticationType));

    [Fact]
    public void Unauthenticated_is_anonymous()
        => Assert.Equal("anonymous", Resolver.GetOwner(new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", "x")]))));

    [Fact]
    public void Bearer_is_scoped_to_tenant_and_app()
    {
        Assert.Equal("tenant:t1/app:client-a", Resolver.GetOwner(User(("tid", "t1"), ("azp", "client-a"), ("appid", "other"), ("sub", "s"))));
        Assert.Equal("tenant:t1/app:client-b", Resolver.GetOwner(User(("tid", "t1"), ("appid", "client-b"), ("sub", "s"))));
        Assert.Equal("tenant:t1/app:s1", Resolver.GetOwner(User(("tid", "t1"), ("sub", "s1"), ("oid", "o1"))));
        Assert.Equal("tenant:t1/app:o1", Resolver.GetOwner(User(("tid", "t1"), ("oid", "o1"))));
        Assert.Equal("tenant:t2/app:a", Resolver.GetOwner(User((CallerIdentity.MappedTenantIdClaim, "t2"), ("azp", "a"))));
    }

    [Fact]
    public void Bearer_with_tenant_but_no_app_or_subject_is_rejected()
        => Assert.Equal(401, Assert.Throws<A2AAuthException>(() => Resolver.GetOwner(User(("tid", "t1")))).HttpStatus);

    [Fact]
    public void Api_key_uses_key_name_only_for_the_api_key_identity()
    {
        Assert.Equal("apikey:dev", Resolver.GetOwner(UserOf("ApiKey", (CallerIdentity.ApiKeyClaim, "dev"))));
        // A bearer token that happens to carry the claim is not an API key caller.
        Assert.Equal("tenant:t1/app:a", Resolver.GetOwner(User((CallerIdentity.ApiKeyClaim, "dev"), ("tid", "t1"), ("azp", "a"))));
        Assert.Equal("user:s1", Resolver.GetOwner(User((CallerIdentity.ApiKeyClaim, "dev"), ("sub", "s1"))));
        Assert.Equal("apikey:k", new DefaultOwnerResolver("Custom").GetOwner(UserOf("Custom", (CallerIdentity.ApiKeyClaim, "k"))));
    }

    [Fact]
    public void Authenticated_without_tenant_or_key_uses_subject()
        => Assert.Equal("user:s1", Resolver.GetOwner(User(("sub", "s1"))));

    [Fact]
    public void Authenticated_without_any_identifier_is_rejected()
    {
        var ex = Assert.Throws<A2AAuthException>(() => Resolver.GetOwner(User(("role", "x"))));
        Assert.Equal(401, ex.HttpStatus);
    }

    [Fact]
    public void Caller_identity_is_a_shortcut_to_the_default_resolver()
        => Assert.Equal("tenant:t/app:a", CallerIdentity.GetOwner(User(("tid", "t"), ("azp", "a"))));

    [Theory]
    [InlineData("tenant:t1/app:a", "tenant:t1/app:b", TaskState.AuthRequired, true)]
    [InlineData("tenant:t1/app:a", "tenant:t1/app:b", TaskState.InputRequired, false)]
    [InlineData("tenant:t1/app:a", "tenant:t1/app:b", TaskState.Working, false)]
    [InlineData("tenant:t1/app:a", "tenant:t1/app:b", TaskState.Completed, false)]
    [InlineData("tenant:t1/app:a", "tenant:t2/app:b", TaskState.AuthRequired, false)]
    [InlineData("tenant:t1/app:a", "tenant:t1x/app:b", TaskState.AuthRequired, false)]
    [InlineData("tenant:t1/app:a", "apikey:dev", TaskState.AuthRequired, false)]
    [InlineData("apikey:dev", "apikey:other", TaskState.AuthRequired, false)]
    [InlineData("anonymous", "tenant:t1/app:a", TaskState.AuthRequired, false)]
    [InlineData("user:s1", "user:s2", TaskState.AuthRequired, false)]
    public void Only_auth_required_tasks_can_be_continued_by_another_app_of_the_same_tenant(string taskOwner, string caller, TaskState state, bool expected)
        => Assert.Equal(expected, Resolver.CanContinueInterrupted(taskOwner, caller, state));
}
