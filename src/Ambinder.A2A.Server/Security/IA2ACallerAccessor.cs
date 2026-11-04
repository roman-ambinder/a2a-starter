namespace Ambinder.A2A.Server.Security;

/// <summary>Gives services deep inside an agent run access to the caller of that run.</summary>
/// <remarks>
/// <para>
/// Lifetime: the implementation is a singleton registered by <c>AddA2AServerCore</c>. Inject it into any service, including
/// singletons; it keeps no per-run state itself.
/// </para>
/// <para>
/// Concurrency: <see cref="Current"/> flows with the async execution context (an <see cref="System.Threading.AsyncLocal{T}"/>). The
/// task manager sets it around each agent execution and each agent cancel, so concurrent runs of different callers each see their
/// own caller. Work you start from the agent with <c>Task.Run</c> or <c>await</c> keeps the value; work that suppresses the
/// execution context flow does not.
/// </para>
/// <para>
/// The value stays set for the whole run, also after the HTTP request that started it has ended. Outside a run (for example in
/// a request handler or a hosted service) it is <see langword="null"/>.
/// </para>
/// </remarks>
public interface IA2ACallerAccessor
{
    /// <summary>The caller of the agent run in progress, or <see langword="null"/> when no run is in progress on this execution context.</summary>
    A2ACaller? Current { get; }
}
