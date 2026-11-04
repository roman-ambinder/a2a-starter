using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Security;
using Ambinder.A2A.Server.Tasks;

namespace Ambinder.A2A.Server.Tests.Security;

public class CallContextTests
{
    private static A2ACaller Caller(string owner, string? token = null) => new()
    {
        Owner = owner,
        Kind = CallerKind.Application,
        Principal = new ClaimsPrincipal(new ClaimsIdentity()),
        AccessToken = token,
    };

    [Fact]
    public void Context_has_no_Owner_or_User_property()
    {
        var names = typeof(A2ACallContext).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("Owner", names);
        Assert.DoesNotContain("User", names);
        Assert.Contains("Caller", names);
    }

    [Fact]
    public void Context_is_a_property_record_without_a_positional_constructor()
    {
        Assert.All(typeof(A2ACallContext).GetConstructors(), c => Assert.Empty(c.GetParameters()));
        foreach (var name in new[] { "AgentName", "Caller", "Tenant" })
            Assert.NotNull(typeof(A2ACallContext).GetProperty(name)!.GetCustomAttribute<RequiredMemberAttribute>());
        Assert.Equal(typeof(string), typeof(A2ACallContext).GetProperty("Tenant")!.PropertyType);
    }

    [Fact]
    public void ToString_does_not_show_the_access_token()
    {
        var call = TestHost.Call() with { Caller = Caller("tid:t/app:a", token: "s3cr3t-token") };

        Assert.DoesNotContain("s3cr3t-token", call.ToString(), StringComparison.Ordinal);
        Assert.Contains("AccessToken = ***", call.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Task_manager_isolates_tasks_by_the_callers_owner()
    {
        var host = new TestHost();
        var task = (await host.SendAsync(TestHost.Text("hi"), owner: "tid:t1/app:a")).Task!;

        Assert.Equal(task.Id, (await host.GetAsync(task.Id, owner: "tid:t1/app:a")).Id);
        var ex = await Assert.ThrowsAsync<A2AException>(() => host.GetAsync(task.Id, owner: "tid:t1/app:b"));
        Assert.Equal(A2AErrorCode.TaskNotFound, ex.Code);
    }

    [Fact]
    public void Security_options_have_the_documented_defaults()
    {
        var options = new A2ASecurityOptions();

        Assert.False(options.KeepCallerToken);
        Assert.True(options.FilterExtendedCardSkills);
        Assert.Empty(options.Tenants);
        Assert.Empty(options.TenantBindings);
    }
}
