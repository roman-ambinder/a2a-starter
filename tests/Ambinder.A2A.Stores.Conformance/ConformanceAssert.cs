using System.Diagnostics.CodeAnalysis;

namespace Ambinder.A2A.Testing.Conformance;

/// <summary>Minimal assertions so the suite needs no assertion package. Failures name the case id and both values.</summary>
internal static class ConformanceAssert
{
    /// <summary>The id of the case running on this async flow; set by the xUnit classes.</summary>
    internal static readonly AsyncLocal<string?> CurrentCase = new();

    internal static void True([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw Fail(message);
    }

    internal static void False([DoesNotReturnIf(true)] bool condition, string message)
    {
        if (condition) throw Fail(message);
    }

    internal static void NotNull<T>([NotNull] T? value, string message) where T : class
    {
        if (value is null) throw Fail(message);
    }

    internal static void Null(object? value, string message)
    {
        if (value is not null) throw Fail($"{message} Actual: <{Show(value)}>.");
    }

    internal static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw Fail($"{message} Expected: <{Show(expected)}>. Actual: <{Show(actual)}>.");
    }

    internal static void SequenceEqual<T>(IEnumerable<T>? expected, IEnumerable<T>? actual, string message)
    {
        if (expected is null || actual is null)
        {
            if (expected is null && actual is null) return;
            throw Fail($"{message} Expected: {(expected is null ? "null" : "a sequence")}. Actual: {(actual is null ? "null" : "a sequence")}.");
        }

        var e = expected.ToList();
        var a = actual.ToList();
        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < Math.Min(e.Count, a.Count); i++)
        {
            if (!comparer.Equals(e[i], a[i]))
                throw Fail($"{message} First difference at index {i}: expected <{Show(e[i])}>, actual <{Show(a[i])}>. Expected: [{Join(e)}]. Actual: [{Join(a)}].");
        }

        if (e.Count != a.Count)
            throw Fail($"{message} Lengths differ (expected {e.Count}, actual {a.Count}); first difference at index {Math.Min(e.Count, a.Count)}. Expected: [{Join(e)}]. Actual: [{Join(a)}].");
    }

    /// <summary>
    /// Asserts <paramref name="action"/> throws exactly <typeparamref name="TException"/> (an exact type match: derived types fail).
    /// Skip and failure exceptions pass through untouched; a cancellation is let through unless it is the expected type.
    /// </summary>
    internal static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string message)
        where TException : Exception
    {
        try { await action().ConfigureAwait(false); }
        catch (Exception ex) when (ex is ConformanceSkipException or ConformanceFailedException) { throw; }
        catch (Exception ex) when (ex.GetType() == typeof(TException)) { return (TException)ex; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw Fail($"{message} Expected exactly {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}", ex);
        }

        throw Fail($"{message} Expected {typeof(TException).Name}, but nothing was thrown.");
    }

    private static string Show(object? value) => value is null ? "null" : value.ToString() ?? "";

    private static string Join<T>(List<T> items) => string.Join(", ", items.Select(i => Show(i)));

    private static ConformanceFailedException Fail(string message, Exception? inner = null) =>
        new(CurrentCase.Value is { } id ? $"[{id}] {message}" : message, inner);
}
