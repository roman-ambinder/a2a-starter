using Xunit;

namespace Ambinder.A2A.Testing.Conformance;

/// <summary>Derive this class and override <see cref="CreateStoresAsync"/> to run the Outbox store conformance suite.</summary>
public abstract class OutboxStoreConformanceTests
{
    /// <summary>Return a fresh, empty store set (and the suite's FakeTimeProvider). Called once per test.</summary>
    protected abstract ValueTask<StoreSet> CreateStoresAsync();

    /// <summary>The case ids, one test each.</summary>
    public static TheoryData<string> CaseIds { get; } = new(ConformanceCatalog.UniqueIds(OutboxStoreConformanceCases.All));

    /// <summary>Runs one conformance case.</summary>
    [Theory(SkipExceptions = [typeof(ConformanceSkipException)])]
    [MemberData(nameof(CaseIds))]
    public async Task Case(string id)
    {
        var c = ConformanceCatalog.Find(OutboxStoreConformanceCases.All, id);
        ConformanceAssert.CurrentCase.Value = id;
        await using var stores = await CreateStoresAsync();
        if (c.SkipReason?.Invoke(stores.Capabilities) is { } reason) throw new ConformanceSkipException(reason);
        await c.Run(stores, TestContext.Current.CancellationToken);
    }
}
