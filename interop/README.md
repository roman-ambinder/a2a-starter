# Interop

Cross-SDK interoperability checks for Ambinder.A2A: fixtures and a contract (`contract.md`), the support matrix
(`matrix.json`), tolerated peer differences (`known-divergences.json`) and the test project
`tests/Ambinder.A2A.Interop.Tests`. Peer SDKs run as containers built from `interop/peers/<peer>/` (added in later tasks).
The CI entry point is `.github/workflows/interop.yml`, called from `ci.yml`.

## Trait convention

- Tests that need Docker (peer containers) carry `[Trait("Category", "Interop")]`.
- Fast tests that must run on every pull request carry `[Trait("Tier", "Smoke")]` (none yet).

## Unit job: skip Docker tests

The solution-wide test run in `ci.yml` (`build-and-test`) excludes Docker tests with the Microsoft.Testing.Platform /
xUnit v3 option `--filter-not-trait` (confirmed with `dotnet test --help`; it takes `name=value` pairs):

```
dotnet test --solution Ambinder.A2A.slnx --no-build -c Release --filter-not-trait "Category=Interop"
```

The positive counterpart is `--filter-trait "Name=Value"`. Note that the runner exits with code 8 when a filter matches
zero tests, so a trait filter is only usable once at least one test carries that trait.

## Run locally

```
dotnet test --project tests/Ambinder.A2A.Interop.Tests -c Release                                   # everything that exists
dotnet test --project tests/Ambinder.A2A.Interop.Tests -c Release --filter-not-trait "Category=Interop"   # without Docker
dotnet test --project tests/Ambinder.A2A.Interop.Tests -c Release --filter-trait "Category=Interop"       # Docker tests only
```

Always use `-c Release`.
