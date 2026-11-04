using Ambinder.A2A.Server.Stores;
using Xunit;

namespace Ambinder.A2A.Server.Tests.Stores;

public class StoreOptionsTests
{
    [Fact]
    public void Defaults_match_spec()
    {
        var o = new A2AStoreOptions();
        Assert.Equal(TimeSpan.FromDays(30), o.TerminalTaskTtl);
        Assert.Equal(TimeSpan.FromDays(30), o.InterruptedTaskTtl);
        Assert.Equal(TimeSpan.FromMinutes(5), o.PurgeInterval);
        Assert.Equal(500, o.PurgeBatchSize);

        var m = new InMemoryTaskStoreOptions();
        Assert.Equal(10_000, m.MaxTasks);
        Assert.Equal(TimeSpan.FromHours(1), m.TerminalTaskTtl);
        Assert.Equal(50_000, m.MaxPushConfigs);
    }
}
