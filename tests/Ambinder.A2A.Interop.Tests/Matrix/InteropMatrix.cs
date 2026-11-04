using System.Text.Json;

namespace Ambinder.A2A.Interop.Tests.Matrix;

/// <summary>A scenario as the matrix sees it: an id and the protocol versions it supports. Independent of the scenario classes.</summary>
public sealed record ScenarioDescriptor(string Id, IReadOnlyList<string> ProtocolVersions);

/// <summary>A peer entry of <c>matrix.json</c>.</summary>
public sealed record PeerInfo(string Kind, IReadOnlyList<string> ProtocolVersions, IReadOnlyList<string> Transports);

/// <summary>A reviewed skip. <c>*</c> is allowed in transport, direction and scenario.</summary>
public sealed record SkipEntry(string Peer, string ProtocolVersion, string Transport, string Direction, string Scenario, string Reason, string? Upstream);

/// <summary>The smoke subset (cells run on every PR).</summary>
public sealed record SmokeSpec(IReadOnlyList<string> Transports, IReadOnlyList<string> Scenarios, IReadOnlyList<string> Peers);

/// <summary>One run of a scenario. <see cref="Tier"/> is the xUnit trait value (<c>Tier=Smoke</c> or <c>Tier=Full</c>).</summary>
public sealed record MatrixCell(string Peer, string ProtocolVersion, string Transport, string Direction, string ScenarioId, string? SkipReason, bool IsSmoke)
{
    public string Tier => IsSmoke ? "Smoke" : "Full";

    /// <summary>Readable id for test display names.</summary>
    public override string ToString() => $"{Peer}/{ProtocolVersion}/{Transport}/{Direction}/{ScenarioId}";
}

/// <summary>
/// Loads <c>interop/matrix.json</c> and expands it to cells: peer x protocol version x transport x direction x scenario.
/// A cell exists where the peer and the scenario both support the version. Directions are <c>A</c> (our client, peer server)
/// and <c>B</c> (peer client, our server).
/// </summary>
public sealed class InteropMatrix
{
    public static readonly string[] Directions = ["A", "B"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web) { ReadCommentHandling = JsonCommentHandling.Skip };

    public IReadOnlyDictionary<string, PeerInfo> Peers { get; }
    public IReadOnlyList<SkipEntry> Skips { get; }
    public SmokeSpec Smoke { get; }

    private InteropMatrix(IReadOnlyDictionary<string, PeerInfo> peers, IReadOnlyList<SkipEntry> skips, SmokeSpec smoke)
        => (Peers, Skips, Smoke) = (peers, skips, smoke);

    /// <summary>Parses and validates a matrix document. Throws <see cref="InvalidDataException"/> for a skip without reason or for an unknown peer.</summary>
    public static InteropMatrix Load(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var peers = root.GetProperty("peers").Deserialize<Dictionary<string, PeerInfo>>(Json)
                    ?? throw new InvalidDataException("matrix has no peers");
        var skips = root.TryGetProperty("skips", out var s) ? s.Deserialize<List<SkipEntry>>(Json) ?? [] : [];
        var smoke = root.TryGetProperty("smoke", out var sm) ? sm.Deserialize<SmokeSpec>(Json)! : new SmokeSpec([], [], []);

        foreach (var skip in skips)
        {
            if (string.IsNullOrWhiteSpace(skip.Reason))
                throw new InvalidDataException($"skip for {skip.Peer}/{skip.Scenario} has no reason");
            if (!peers.ContainsKey(skip.Peer))
                throw new InvalidDataException($"skip names unknown peer '{skip.Peer}'");
        }
        foreach (var p in smoke.Peers.Where(p => !peers.ContainsKey(p)))
            throw new InvalidDataException($"smoke names unknown peer '{p}'");

        return new InteropMatrix(peers, skips, smoke);
    }

    /// <summary>Loads <c>interop/matrix.json</c> from the repository.</summary>
    public static InteropMatrix LoadDefault()
        => Load(File.ReadAllText(Path.Combine(RepoRoot(), "interop", "matrix.json")));

    /// <summary>Walks up from <see cref="AppContext.BaseDirectory"/> to the folder that holds <c>Ambinder.A2A.slnx</c>.</summary>
    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Ambinder.A2A.slnx")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Ambinder.A2A.slnx not found above " + AppContext.BaseDirectory);
    }

    /// <summary>All cells, skipped ones included (they carry <see cref="MatrixCell.SkipReason"/>).</summary>
    public IEnumerable<MatrixCell> Cells(IReadOnlyList<ScenarioDescriptor> scenarios)
    {
        foreach (var (peerName, peer) in Peers)
        foreach (var version in peer.ProtocolVersions)
        foreach (var transport in peer.Transports)
        foreach (var direction in Directions)
        foreach (var scenario in scenarios.Where(sc => sc.ProtocolVersions.Contains(version)))
            yield return new MatrixCell(peerName, version, transport, direction, scenario.Id,
                SkipReasonFor(peerName, version, transport, direction, scenario.Id), IsSmoke(peerName, transport, scenario.Id));
    }

    /// <summary>Only the smoke cells (<c>smoke</c> in matrix.json).</summary>
    public IEnumerable<MatrixCell> SmokeCells(IReadOnlyList<ScenarioDescriptor> scenarios) => Cells(scenarios).Where(c => c.IsSmoke);

    private bool IsSmoke(string peer, string transport, string scenario)
        => Smoke.Peers.Contains(peer) && Smoke.Transports.Contains(transport) && Smoke.Scenarios.Contains(scenario);

    private string? SkipReasonFor(string peer, string version, string transport, string direction, string scenario)
        => Skips.FirstOrDefault(s => s.Peer == peer && Any(s.ProtocolVersion, version) && Any(s.Transport, transport)
                                     && Any(s.Direction, direction) && Any(s.Scenario, scenario))?.Reason;

    private static bool Any(string pattern, string value) => pattern == "*" || pattern == value;
}
