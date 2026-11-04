using Microsoft.Extensions.Time.Testing;

namespace Ambinder.A2A.Interop.Tests.Matrix;

public class LedgerTests
{
    static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));

    static Divergence Entry(string expires = "2027-01-01") =>
        new("fake-1", "python", "1.3.0", "scenario:DATA-01", "jsonrpc", "A", "fake", "https://example.invalid/1", DateOnly.Parse(expires));

    [Fact]
    public void Unlisted_difference_fails()
    {
        var verdict = DivergenceLedger.Check([], [new ObservedDifference("python", "scenario:DATA-01", "jsonrpc", "A", "x")], Clock);
        Assert.Single(verdict.Unlisted);
    }

    [Fact]
    public void Listed_difference_passes()
    {
        var verdict = DivergenceLedger.Check([Entry()], [new ObservedDifference("python", "scenario:DATA-01", "jsonrpc", "A", "x")], Clock);
        Assert.True(verdict.Ok);
    }

    [Fact]
    public void Entry_that_no_longer_reproduces_fails()
    {
        var verdict = DivergenceLedger.Check([Entry()], [], Clock, ranTargets: [("python", "scenario:DATA-01", "jsonrpc", "A")]);
        Assert.Single(verdict.Stale);
    }

    [Fact]
    public void Entry_not_run_this_time_is_not_stale()
        => Assert.True(DivergenceLedger.Check([Entry()], [], Clock, ranTargets: []).Ok);

    [Fact]
    public void Expired_entry_fails()
    {
        var verdict = DivergenceLedger.Check([Entry(expires: "2026-11-02")], [new ObservedDifference("python", "scenario:DATA-01", "jsonrpc", "A", "x")], Clock);
        Assert.Single(verdict.Expired);
    }

    [Fact]
    public void Wildcards_in_an_entry_match_any_transport_and_direction()
    {
        var wild = Entry() with { Transport = "*", Direction = "*" };
        var verdict = DivergenceLedger.Check([wild], [new ObservedDifference("python", "scenario:DATA-01", "grpc", "B", "x")], Clock);
        Assert.True(verdict.Ok);
    }

    [Fact]
    public void A_different_peer_does_not_match()
    {
        var verdict = DivergenceLedger.Check([Entry()], [new ObservedDifference("js", "scenario:DATA-01", "jsonrpc", "A", "x")], Clock);
        Assert.Single(verdict.Unlisted);
    }

    [Fact]
    public void The_ledger_file_in_the_repo_loads()
        => Assert.NotNull(DivergenceLedger.LoadDefault());
}
