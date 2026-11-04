using System.Text.Json;

namespace Ambinder.A2A.Interop.Tests.Matrix;

/// <summary>One reviewed cross-SDK difference in <c>interop/known-divergences.json</c>. <c>*</c> is allowed in target, transport and direction.</summary>
public sealed record Divergence(string Id, string Peer, string SdkVersion, string Target, string Transport, string Direction, string What, string Upstream, DateOnly Expires);

/// <summary>A difference a run actually saw.</summary>
public sealed record ObservedDifference(string Peer, string Target, string Transport, string Direction, string Detail);

/// <summary>Result of <see cref="DivergenceLedger.Check"/>.</summary>
public sealed record LedgerVerdict(IReadOnlyList<ObservedDifference> Unlisted, IReadOnlyList<Divergence> Stale, IReadOnlyList<Divergence> Expired)
{
    public bool Ok => Unlisted.Count == 0 && Stale.Count == 0 && Expired.Count == 0;
}

/// <summary>
/// The divergence rules: a difference not in the ledger fails, an entry that no longer reproduces fails, an expired entry fails.
/// A pure function; matching is on peer + target + transport + direction with <c>*</c> wildcards in the entry.
/// </summary>
public static class DivergenceLedger
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web) { ReadCommentHandling = JsonCommentHandling.Skip };

    /// <param name="entries">The ledger.</param>
    /// <param name="observed">Differences seen in this run.</param>
    /// <param name="time">Clock for the expiry check (UTC date).</param>
    /// <param name="ranTargets">(peer, target, transport, direction) combinations this run executed. An entry no run covered cannot be stale. Null means every entry was covered.</param>
    public static LedgerVerdict Check(
        IReadOnlyList<Divergence> entries,
        IReadOnlyList<ObservedDifference> observed,
        TimeProvider time,
        IReadOnlyList<(string Peer, string Target, string Transport, string Direction)>? ranTargets = null)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

        var unlisted = observed.Where(o => !entries.Any(e => Matches(e, o.Peer, o.Target, o.Transport, o.Direction))).ToList();
        var expired = entries.Where(e => e.Expires < today).ToList();
        var stale = entries
            .Where(e => !observed.Any(o => Matches(e, o.Peer, o.Target, o.Transport, o.Direction)))
            .Where(e => ranTargets is null || ranTargets.Any(r => Matches(e, r.Peer, r.Target, r.Transport, r.Direction)))
            .ToList();
        return new LedgerVerdict(unlisted, stale, expired);
    }

    /// <summary>Whether <paramref name="e"/> covers the given combination. A <c>*</c> in the entry matches anything.</summary>
    public static bool Matches(Divergence e, string peer, string target, string transport, string direction)
        => Eq(e.Peer, peer) && Eq(e.Target, target) && Eq(e.Transport, transport) && Eq(e.Direction, direction);

    private static bool Eq(string pattern, string value) => pattern == "*" || string.Equals(pattern, value, StringComparison.Ordinal);

    /// <summary>Parses the ledger JSON (<c>{"entries":[...]}</c>).</summary>
    public static IReadOnlyList<Divergence> Load(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("entries", out var entries))
            throw new InvalidDataException("known-divergences.json has no 'entries' array");
        var list = entries.Deserialize<List<Divergence>>(Json) ?? [];
        foreach (var e in list)
            if (string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.Upstream) || string.IsNullOrWhiteSpace(e.What))
                throw new InvalidDataException($"divergence '{e.Id}' needs id, what and upstream");
        return list;
    }

    /// <summary>Loads <c>interop/known-divergences.json</c> from the repository.</summary>
    public static IReadOnlyList<Divergence> LoadDefault()
        => Load(File.ReadAllText(Path.Combine(InteropMatrix.RepoRoot(), "interop", "known-divergences.json")));
}
