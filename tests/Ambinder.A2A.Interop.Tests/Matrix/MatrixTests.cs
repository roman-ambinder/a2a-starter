namespace Ambinder.A2A.Interop.Tests.Matrix;

public class MatrixTests
{
    // Scenarios arrive in Task 11; until then a small descriptor list stands in. InteropMatrix does not know IScenario.
    static readonly ScenarioDescriptor[] Scenarios =
    [
        new("CARD-01", ["1.0"]),
        new("SEND-02", ["1.0"]),
        new("STREAM-01", ["1.0"]),
        new("ERR-01", ["1.0"]),
    ];

    const string Minimal = """
        {
          "peers": {
            "self": { "kind": "in-process", "protocolVersions": ["1.0"], "transports": ["jsonrpc", "http_json", "grpc"] },
            "python": { "kind": "container", "protocolVersions": ["1.0"], "transports": ["jsonrpc"] }
          },
          "skips": [],
          "smoke": { "transports": ["jsonrpc"], "scenarios": ["CARD-01"], "peers": ["python"] }
        }
        """;

    [Fact]
    public void Cells_expand_peer_version_transport_direction_scenario()
    {
        var cells = InteropMatrix.Load(Minimal).Cells(Scenarios).Where(c => c.Peer == "self").ToList();
        Assert.Equal(1 * 3 * 2 * Scenarios.Length, cells.Count);
        Assert.All(cells, c => Assert.Equal("1.0", c.ProtocolVersion));
        Assert.Equal(["A", "B"], cells.Select(c => c.Direction).Distinct().Order());
    }

    [Fact]
    public void A_peer_limits_its_own_transports()
        => Assert.All(InteropMatrix.Load(Minimal).Cells(Scenarios).Where(c => c.Peer == "python"), c => Assert.Equal("jsonrpc", c.Transport));

    [Fact]
    public void A_new_protocol_version_needs_no_driver_change()
    {
        var json = """
            {
              "peers": {
                "self": { "kind": "in-process", "protocolVersions": ["1.0"], "transports": ["jsonrpc"] },
                "old": { "kind": "container", "protocolVersions": ["0.3"], "transports": ["jsonrpc"] }
              },
              "skips": []
            }
            """;
        ScenarioDescriptor[] scenarios = [new("CARD-01", ["1.0", "0.3"]), new("ONLY-10", ["1.0"])];
        var cells = InteropMatrix.Load(json).Cells(scenarios).ToList();
        var old = cells.Where(c => c.Peer == "old").ToList();
        Assert.Equal(2, old.Count); // CARD-01 x directions A and B; ONLY-10 does not support 0.3
        Assert.All(old, c => { Assert.Equal("0.3", c.ProtocolVersion); Assert.Equal("CARD-01", c.ScenarioId); });
        Assert.Equal(4, cells.Count(c => c.Peer == "self")); // 2 scenarios x 2 directions
    }

    [Fact]
    public void Skip_marks_matching_cells_with_the_reason()
    {
        var json = Minimal.Replace("\"skips\": []", """
            "skips": [{ "peer": "python", "protocolVersion": "1.0", "transport": "*", "direction": "B", "scenario": "SEND-02", "reason": "no runner yet", "upstream": null }]
            """);
        var cells = InteropMatrix.Load(json).Cells(Scenarios).ToList();
        var skipped = cells.Where(c => c.SkipReason is not null).ToList();
        var cell = Assert.Single(skipped);
        Assert.Equal(("python", "B", "SEND-02"), (cell.Peer, cell.Direction, cell.ScenarioId));
        Assert.Equal("no runner yet", cell.SkipReason);
    }

    [Fact]
    public void Skip_needs_reason()
    {
        var json = Minimal.Replace("\"skips\": []", """
            "skips": [{ "peer": "python", "protocolVersion": "1.0", "transport": "*", "direction": "*", "scenario": "*", "reason": " ", "upstream": null }]
            """);
        Assert.Throws<InvalidDataException>(() => InteropMatrix.Load(json));
    }

    [Fact]
    public void Unknown_peer_in_skip_fails()
    {
        var json = Minimal.Replace("\"skips\": []", """
            "skips": [{ "peer": "cobol", "protocolVersion": "1.0", "transport": "*", "direction": "*", "scenario": "*", "reason": "because", "upstream": null }]
            """);
        Assert.Throws<InvalidDataException>(() => InteropMatrix.Load(json));
    }

    [Fact]
    public void Smoke_subset_is_selectable_and_tagged()
    {
        var all = InteropMatrix.Load(Minimal).Cells(Scenarios).ToList();
        var smoke = InteropMatrix.Load(Minimal).SmokeCells(Scenarios).ToList();
        Assert.Equal(2, smoke.Count); // python x jsonrpc x CARD-01 x A,B
        Assert.All(smoke, c => { Assert.True(c.IsSmoke); Assert.Equal("Smoke", c.Tier); Assert.Equal("python", c.Peer); });
        Assert.Equal(smoke.Count, all.Count(c => c.IsSmoke));
        Assert.All(all.Where(c => !c.IsSmoke), c => Assert.Equal("Full", c.Tier));
    }

    [Fact]
    public void The_matrix_file_in_the_repo_loads_and_smoke_is_the_documented_subset()
    {
        ScenarioDescriptor[] all = [new("CARD-01", ["1.0"]), new("SEND-02", ["1.0"]), new("STREAM-01", ["1.0"]), new("ERR-01", ["1.0"]), new("DATA-01", ["1.0"])];
        var matrix = InteropMatrix.LoadDefault();
        Assert.Equal(["java", "js", "python", "self"], matrix.Peers.Keys.Order());
        var smoke = matrix.SmokeCells(all).ToList();
        Assert.Equal(3 * 4 * 2, smoke.Count);
        Assert.All(smoke, c => Assert.Equal("jsonrpc", c.Transport));
    }
}
