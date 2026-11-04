using System.Diagnostics.CodeAnalysis;

namespace Ambinder.A2A.Server;

/// <summary>The rest of the pipeline after a middleware. Call it once to continue, or do not call it to stop the operation.</summary>
/// <param name="context">The operation context. Pass the one you received.</param>
/// <returns>The result of the operation.</returns>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The name is fixed by the A2A hosting design (spec 09 section 7.11) and shared with other SDK packages.")]
public delegate ValueTask<A2AOperationResult> A2AOperationDelegate(A2AOperationContext context);

/// <summary>
/// Code that runs around every A2A operation: for all transports and for in-process calls. Register it with
/// <c>UseMiddleware&lt;T&gt;()</c> on the server builder (all agents) or in the <c>AddAgent</c> options (one agent).
/// </summary>
/// <remarks>
/// Lifetime: the server resolves one instance per call from the request scope, so register dependencies with any lifetime and the
/// type itself needs no registration (it is added as scoped). Concurrency: calls overlap, but one instance serves one call, so
/// instance fields are safe. Shared state in singletons must be thread-safe. Middleware runs in registration order, global
/// middleware first. To reject a call, throw <see cref="Protocol.A2AException"/>. A streaming result must not be buffered: wrap
/// <see cref="A2AOperationResult.Events"/> in your own async iterator instead.
/// </remarks>
public interface IA2AMiddleware
{
    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The operation, the request and the call.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <returns>The result of the operation, usually the one <paramref name="next"/> returned.</returns>
    /// <exception cref="Protocol.A2AException">Throw it to reject the call.</exception>
    ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next);
}
