namespace Ambinder.A2A.Server.Stores;

/// <summary>Where a test makes a write fail.</summary>
public enum StoreFaultPoint
{
    /// <summary>After the task part of a write and before its outbox part.</summary>
    AfterTaskBeforeOutbox,
}

/// <summary>Test hook: makes the next write fail at a point, so atomicity can be tested. Never registered in production.</summary>
public interface IStoreFaultInjector
{
    /// <summary>Makes the next write fail at <paramref name="point"/>.</summary>
    /// <param name="point">Where to fail.</param>
    void FailNextWrite(StoreFaultPoint point);
}
