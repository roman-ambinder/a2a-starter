using System.Diagnostics.CodeAnalysis;

namespace Ambinder.A2A.Testing.Conformance;

/// <summary>Minimal assertions so the suite needs no assertion package. Failures name the case id and both values.</summary>
internal static class ConformanceAssert
{
    /// <summary>The id of the case running on this async flow; set by the xUnit classes.</summary>
    internal static readonly AsyncLocal<string?> CurrentCase = new();

    internal static void True(bool condition, string message)
    {
        if (!condition) throw Fail(message);
    }

    internal static void False(bool condition, string message) => True(!condition, message);

    internal static void NotNull<T>([NotNull] T? value, string message) where T : class
    {
        if (value is null) throw Fail(message);
    }

    internal static void Null(object? value, string message)
    {
        if (value is not null) throw Fail($"{message} Actual: <{value}>.");
    }

    internal static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw Fail($"{message} Expected: <{expected}>. Actual: <{actual}>.");
    }

    internal static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    {
        var e = expected.ToList();
        var a = actual.ToList();
        if (!e.SequenceEqual(a))
            throw Fail($"{message} Expected: [{string.Join(", ", e)}]. Actual: [{string.Join(", ", a)}].");
    }

    internal static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string message)
        where TException : Exception
    {
        try { await action().ConfigureAwait(false); }
        catch (TException ex) { return ex; }
        catch (Exception ex) { throw Fail($"{message} Expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}"); }
        throw Fail($"{message} Expected {typeof(TException).Name}, but nothing was thrown.");
    }

    private static ConformanceFailedException Fail(string message) =>
        new(CurrentCase.Value is { } id ? $"[{id}] {message}" : message);
}
