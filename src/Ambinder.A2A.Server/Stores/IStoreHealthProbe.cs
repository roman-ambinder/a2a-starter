namespace Ambinder.A2A.Server.Stores;

/// <summary>Optional readiness check, used by package 09's <c>a2a-store</c> health check.</summary>
public interface IStoreHealthProbe
{
    /// <summary>Checks that the store can be reached.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>True when the store is healthy.</returns>
    ValueTask<bool> IsHealthyAsync(CancellationToken ct);
}
