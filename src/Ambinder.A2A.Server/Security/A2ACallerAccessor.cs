namespace Ambinder.A2A.Server.Security;

/// <summary>
/// <see cref="IA2ACallerAccessor"/> backed by one process-wide <see cref="AsyncLocal{T}"/>. The task manager is the only writer:
/// it sets the value around a run, so sharing the slot between hosts in one process is harmless.
/// </summary>
internal sealed class A2ACallerAccessor : IA2ACallerAccessor
{
    private static readonly AsyncLocal<A2ACaller?> Slot = new();

    public A2ACaller? Current => Slot.Value;

    /// <summary>Sets the caller on the current execution context. Dispose the result to restore the previous value.</summary>
    internal static Scope Set(A2ACaller caller)
    {
        var previous = Slot.Value;
        Slot.Value = caller;
        return new Scope(previous);
    }

    internal readonly struct Scope(A2ACaller? previous) : IDisposable
    {
        public void Dispose() => Slot.Value = previous;
    }
}
