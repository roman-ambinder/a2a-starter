namespace Ambinder.A2A.Testing.Conformance;

/// <summary>Lookup and integrity checks for a case catalog.</summary>
internal static class ConformanceCatalog
{
    /// <summary>Returns the single case with <paramref name="id"/>; throws a message naming the id when none or several match.</summary>
    internal static ConformanceCase Find(IReadOnlyList<ConformanceCase> cases, string id)
    {
        ConformanceCase? found = null;
        var count = 0;
        foreach (var c in cases)
        {
            if (!string.Equals(c.Id, id, StringComparison.Ordinal)) continue;
            found ??= c;
            count++;
        }

        if (count == 0) throw new InvalidOperationException($"No conformance case has id '{id}'.");
        if (count > 1) throw new InvalidOperationException($"{count} conformance cases share the id '{id}'; case ids must be unique.");
        return found!;
    }

    /// <summary>Throws when two cases share an id; returns the ids otherwise (for use as theory data).</summary>
    internal static IReadOnlyList<string> UniqueIds(IReadOnlyList<ConformanceCase> cases)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new List<string>();
        foreach (var c in cases)
            if (!seen.Add(c.Id) && !duplicates.Contains(c.Id)) duplicates.Add(c.Id);
        if (duplicates.Count > 0)
            throw new InvalidOperationException($"Duplicate conformance case ids: {string.Join(", ", duplicates.Select(d => $"'{d}'"))}.");
        return cases.Select(c => c.Id).ToList();
    }
}
