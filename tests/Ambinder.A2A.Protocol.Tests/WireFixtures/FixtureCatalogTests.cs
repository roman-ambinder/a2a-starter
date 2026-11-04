using Ambinder.A2A.Tests.WireFixtures;

namespace Ambinder.A2A.Protocol.Tests.WireFixtures;

public class FixtureCatalogTests
{
    [Fact]
    public void Every_fixture_has_a_valid_meta_file()
    {
        var problems = FixtureCatalog.Validate(FixtureCatalog.Root);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Catalog_is_not_empty()
        => Assert.NotEmpty(FixtureCatalog.Load(FixtureCatalog.Root));
}
