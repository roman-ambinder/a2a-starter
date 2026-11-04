using Ambinder.A2A.Server.Stores;

namespace Ambinder.A2A.Testing.Conformance;

/// <summary>One named store case. <see cref="Id"/> is stable across releases (12 §5).</summary>
// A property record, not positional: package 12 publishes this type unchanged in M2, so spec 02 §4 applies.
public sealed record ConformanceCase
{
    /// <summary>The stable case id, for example <c>crud.create-then-get</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Runs the case against a fresh store set.</summary>
    public required Func<StoreSet, CancellationToken, Task> Run { get; init; }

    /// <summary>Returns a reason when the store's capabilities do not allow this case; null to run it.</summary>
    public Func<StoreCapabilities, string?>? SkipReason { get; init; }
}

/// <summary>Thrown by a case when it cannot run on this store (capability missing). Mapped to a skip by the xUnit classes.</summary>
/// <param name="reason">Why the case is skipped.</param>
public sealed class ConformanceSkipException(string reason) : Exception(reason);

/// <summary>Thrown when a conformance assertion fails; the message names the case id and the values.</summary>
public sealed class ConformanceFailedException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The failure message.</param>
    public ConformanceFailedException(string message) : base(message) { }

    /// <summary>Creates the exception wrapping the unexpected exception that caused it.</summary>
    /// <param name="message">The failure message.</param>
    /// <param name="inner">The original exception.</param>
    public ConformanceFailedException(string message, Exception? inner) : base(message, inner) { }
}
