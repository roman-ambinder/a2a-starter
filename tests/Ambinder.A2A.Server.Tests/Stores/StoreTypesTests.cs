using Ambinder.A2A.Server.Stores;
using Xunit;

namespace Ambinder.A2A.Server.Tests.Stores;

public class StoreTypesTests
{
    [Fact]
    public void TaskKey_equality_is_ordinal_and_includes_tenant()
    {
        Assert.NotEqual(new TaskKey("a", "agent", "t1"), new TaskKey("A", "agent", "t1"));
        Assert.NotEqual(new TaskKey("a", "agent", "t1"), new TaskKey("b", "agent", "t1"));
        Assert.Equal(new TaskKey("", "agent", "t1"), new TaskKey("", "agent", "t1"));
    }

    [Fact]
    public void StoredCaller_defaults_to_empty_roles_and_scopes()
    {
        var c = new StoredCaller { Owner = "tid:x/app:y" };
        Assert.Empty(c.Roles);
        Assert.Empty(c.Scopes);
    }

    [Fact]
    public void Store_exception_marks_unavailable_as_transient()
    {
        Assert.True(new A2AStoreException(StoreErrorKind.Unavailable, "down").IsTransient);
        Assert.False(new A2AStoreException(StoreErrorKind.Conflict, "stale").IsTransient);
        Assert.True(new A2AStoreException(StoreErrorKind.Conflict, "throttled", isTransient: true).IsTransient);
    }
}
