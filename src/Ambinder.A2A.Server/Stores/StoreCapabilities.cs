namespace Ambinder.A2A.Server.Stores;

/// <summary>Optional abilities of a store set. The conformance suite skips cases a store does not support.</summary>
public sealed record StoreCapabilities
{
    /// <summary>The store can fail a write between the task part and the outbox part (implements <see cref="IStoreFaultInjector"/>).</summary>
    public bool FaultInjection { get; init; }

    /// <summary>Expiry is done by the database (Cosmos TTL); <c>PurgeExpiredAsync</c> is a no-op.</summary>
    public bool NativeTtl { get; init; }

    /// <summary>Data survives the process; push credentials are protected at rest.</summary>
    public bool Durable { get; init; }

    /// <summary>A task key is unique across owners. False for owner-partitioned stores (Cosmos), where task ids are server GUIDs.</summary>
    public bool KeyUniqueAcrossOwners { get; init; } = true;

    /// <summary>The store can back package 06's cluster mode in 1.0 (B3). True only for EF Core.</summary>
    public bool SupportsClusterMode { get; init; }

    /// <summary>Outbox claims cross partitions (Cosmos). Package 08's dispatcher slows its recovery poll when true.</summary>
    public bool OutboxClaimIsCrossPartition { get; init; }
}

/// <summary>Implemented by a store package's registration so the core and the suite can read its capabilities.</summary>
public interface IStoreCapabilities
{
    /// <summary>The abilities of this store set.</summary>
    StoreCapabilities Capabilities { get; }
}
