namespace Ambinder.A2A.Server.Stores;

/// <summary>What went wrong in a store call.</summary>
public enum StoreErrorKind
{
    /// <summary>The expected version did not match the stored version, or another writer won a race.</summary>
    Conflict,

    /// <summary>A record with the same key already exists.</summary>
    AlreadyExists,

    /// <summary>The record does not exist.</summary>
    NotFound,

    /// <summary>The store is full.</summary>
    Capacity,

    /// <summary>The store cannot be reached or is throttling.</summary>
    Unavailable,
}

/// <summary>The only exception a store throws (besides <see cref="OperationCanceledException"/>). Stores translate provider errors into it.</summary>
public sealed class A2AStoreException : Exception
{
    /// <summary>Creates a store exception.</summary>
    /// <param name="kind">What went wrong.</param>
    /// <param name="message">A message for logs.</param>
    /// <param name="inner">The provider exception that caused this one, if any.</param>
    /// <param name="isTransient">Whether a retry may succeed. When null, true only for <see cref="StoreErrorKind.Unavailable"/>.</param>
    public A2AStoreException(StoreErrorKind kind, string message, Exception? inner = null, bool? isTransient = null)
        : base(message, inner)
    {
        Kind = kind;
        IsTransient = isTransient ?? kind == StoreErrorKind.Unavailable;
    }

    /// <summary>What went wrong.</summary>
    public StoreErrorKind Kind { get; }

    /// <summary>True for <see cref="StoreErrorKind.Unavailable"/> and throttling.</summary>
    public bool IsTransient { get; }
}
