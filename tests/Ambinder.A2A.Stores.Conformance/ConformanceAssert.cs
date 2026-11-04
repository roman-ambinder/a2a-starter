using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Stores;

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

    /// <summary>Asserts <paramref name="action"/> throws an <see cref="A2AStoreException"/> of kind <paramref name="kind"/>.</summary>
    internal static async Task StoreErrorAsync(Func<Task> action, StoreErrorKind kind, string message)
    {
        var ex = await ThrowsAsync<A2AStoreException>(action, message).ConfigureAwait(false);
        if (ex.Kind != kind)
            throw Fail($"{message} Expected store error kind {kind}, got {ex.Kind}: {ex.Message}", ex);
    }

    /// <summary>Asserts <paramref name="action"/> throws something (any exception type except a cancellation, skip or failure).</summary>
    internal static async Task<Exception> ThrowsAnyAsync(Func<Task> action, string message)
    {
        try { await action().ConfigureAwait(false); }
        catch (Exception ex) when (ex is ConformanceSkipException or ConformanceFailedException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ex; }

        throw Fail($"{message} Expected an exception, but nothing was thrown.");
    }

    /// <summary>
    /// Asserts two stored tasks are equal field by field. The <see cref="AgentTask"/> (including <c>Metadata</c> and any other JSON object)
    /// is compared semantically: JSON objects are equal when they hold the same properties with equal values, whatever the property order
    /// (a JSON column such as PostgreSQL <c>jsonb</c> does not keep it). Arrays stay ordered, and so do the lists on the stored task:
    /// the order of <c>Caller.Roles</c>, <c>Caller.Scopes</c> and <c>ContinuationGrant.AllowedOwners</c> is part of the contract.
    /// Instants are compared by UTC ticks. <c>Version</c> is skipped when <paramref name="ignoreVersion"/> is true.
    /// </summary>
    internal static void TaskEquivalent(StoredTask expected, StoredTask? actual, bool ignoreVersion = false)
    {
        if (actual is null) throw Fail($"Expected task '{expected.Key.TaskId}' but the store returned null.");
        Equal(expected.Key, actual.Key, "Key differs.");
        Equal(expected.Owner, actual.Owner, "Owner differs.");
        var expectedJson = JsonSerializer.SerializeToElement(expected.Task, A2AJson.AgentTask);
        var actualJson = JsonSerializer.SerializeToElement(actual.Task, A2AJson.AgentTask);
        if (!JsonSemanticallyEqual(expectedJson, actualJson))
            throw Fail($"Task (AgentTask JSON) differs. Expected: <{expectedJson.GetRawText()}>. Actual: <{actualJson.GetRawText()}>.");
        if (!ignoreVersion) Equal(expected.Version, actual.Version, "Version differs.");
        Equal(expected.Sequence, actual.Sequence, "Sequence differs.");
        Instant(expected.CreatedAt, actual.CreatedAt, "CreatedAt differs.");
        Instant(expected.UpdatedAt, actual.UpdatedAt, "UpdatedAt differs.");
        Instant(expected.ExpiresAt, actual.ExpiresAt, "ExpiresAt differs.");
        Equal(expected.Runner is null, actual.Runner is null, "Runner presence differs.");
        if (expected.Runner is { } er && actual.Runner is { } ar)
        {
            Equal(er.InstanceId, ar.InstanceId, "Runner.InstanceId differs.");
            Instant(er.ClaimedAt, ar.ClaimedAt, "Runner.ClaimedAt differs.");
        }

        Instant(expected.CancelRequestedAt, actual.CancelRequestedAt, "CancelRequestedAt differs.");
        Equal(expected.RunAttempt, actual.RunAttempt, "RunAttempt differs.");
        Equal(expected.OriginMessageId, actual.OriginMessageId, "OriginMessageId differs.");
        Equal(expected.LastRunnerInstanceId, actual.LastRunnerInstanceId, "LastRunnerInstanceId differs.");
        Equal(expected.ContinuationGrant is null, actual.ContinuationGrant is null, "ContinuationGrant presence differs.");
        if (expected.ContinuationGrant is { } eg && actual.ContinuationGrant is { } ag)
        {
            SequenceEqual(eg.AllowedOwners.Select(o => o.Value), ag.AllowedOwners.Select(o => o.Value), "ContinuationGrant.AllowedOwners differ.");
            Instant(eg.ExpiresAt, ag.ExpiresAt, "ContinuationGrant.ExpiresAt differs.");
        }

        Equal(expected.Caller is null, actual.Caller is null, "Caller presence differs.");
        if (expected.Caller is { } ec && actual.Caller is { } ac)
        {
            Equal(ec.Owner, ac.Owner, "Caller.Owner differs.");
            Equal(ec.EntraTenantId, ac.EntraTenantId, "Caller.EntraTenantId differs.");
            Equal(ec.AppId, ac.AppId, "Caller.AppId differs.");
            Equal(ec.UserObjectId, ac.UserObjectId, "Caller.UserObjectId differs.");
            SequenceEqual(ec.Roles, ac.Roles, "Caller.Roles differ.");
            SequenceEqual(ec.Scopes, ac.Scopes, "Caller.Scopes differ.");
        }
    }

    /// <summary>Deep JSON equality: object property order is ignored, array order is significant, numbers compare by value.</summary>
    private static bool JsonSemanticallyEqual(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var left = a.EnumerateObject().ToList();
                var right = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                if (left.Count != right.Count) return false;
                foreach (var p in left)
                {
                    if (!right.TryGetValue(p.Name, out var other) || !JsonSemanticallyEqual(p.Value, other)) return false;
                }

                return true;
            case JsonValueKind.Array:
                var la = a.EnumerateArray().ToList();
                var lb = b.EnumerateArray().ToList();
                if (la.Count != lb.Count) return false;
                for (var i = 0; i < la.Count; i++)
                {
                    if (!JsonSemanticallyEqual(la[i], lb[i])) return false;
                }

                return true;
            case JsonValueKind.String:
                return string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal);
            case JsonValueKind.Number:
                return a.TryGetDecimal(out var da) && b.TryGetDecimal(out var db)
                    ? da == db
                    : string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal);
            default:
                return true; // true, false, null: the kinds already match.
        }
    }

    private static void Instant(DateTimeOffset? expected, DateTimeOffset? actual, string message)
    {
        if (expected is null || actual is null)
        {
            if (expected is null && actual is null) return;
            throw Fail($"{message} Expected: <{Show(expected)}>. Actual: <{Show(actual)}>.");
        }

        Instant(expected.Value, actual.Value, message);
    }

    private static void Instant(DateTimeOffset expected, DateTimeOffset actual, string message)
    {
        if (expected.UtcTicks != actual.UtcTicks)
            throw Fail($"{message} Expected: <{expected:O}>. Actual: <{actual:O}>.");
    }

    private static string Show(object? value) => value is null ? "null" : value.ToString() ?? "";

    private static string Join<T>(List<T> items) => string.Join(", ", items.Select(i => Show(i)));

    private static ConformanceFailedException Fail(string message, Exception? inner = null) =>
        new(CurrentCase.Value is { } id ? $"[{id}] {message}" : message, inner);
}
