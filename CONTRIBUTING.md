# Contributing

Thanks for helping. Please read this page and the [Code of Conduct](CODE_OF_CONDUCT.md) first.

## Prerequisites

- The .NET SDK version pinned in [`global.json`](global.json) (it rolls forward to the latest feature band).
- Tests run on Microsoft.Testing.Platform (set in `global.json`).

## Build and test

```bash
dotnet build Ambinder.A2A.slnx -c Release
dotnet test --solution Ambinder.A2A.slnx -c Release
```

A single test project:

```bash
dotnet test --project tests/Ambinder.A2A.Server.Tests -c Release
```

## Repository layout

| Path | Contents |
|---|---|
| `src/` | The shipped NuGet packages (`Ambinder.A2A.*`). |
| `src/Shared/`, `src/Grpc.Shared/` | Source files linked into several packages and compiled as `internal` in each. |
| `tests/` | Unit, integration, TCK and package-content tests. |
| `samples/` | Sample agents and clients. |
| `eng/` | Build and release tooling (icon, package checks). |
| `proto/` | The vendored A2A proto. |

## Rules

- **No `InternalsVisibleTo` between shipped assemblies.** It is allowed only to the matching test assemblies and to the
  benchmarks assembly. Share small helpers by linking source files from `src/Shared/` instead.
- **New dependencies are visible in review.** Add every new package dependency of a shipped project to
  `eng/package-dependencies.json` in the same PR. The package check fails when the packed dependencies differ from that file.
  Run it locally: `dotnet pack Ambinder.A2A.slnx -c Release -o artifacts/packages`, then
  `dotnet run eng/check-packages.cs -- --packages artifacts/packages --map eng/package-dependencies.json`. Core packages (`Protocol`, `Server`, `Client`) must not depend on
  ASP.NET Core, gRPC, Azure, EF or a database.
- **Public API files.** Workflow for `PublicAPI.*.txt`: added by package 02.
- **Performance rules.** Added by package 14.

## Commits and pull requests

- Keep changes small. One change per PR.
- Add an entry under `## [Unreleased]` in [`CHANGELOG.md`](CHANGELOG.md). Breaking changes go under `### Breaking changes`.
- Add or update tests, and update docs when behavior changes.
- Sign off every commit with the [Developer Certificate of Origin](https://developercertificate.org/):
  `git commit -s` adds a `Signed-off-by: Your Name <you@example.com>` line. There is no CLA.
- Fill in the pull request template checklist.

## Releasing

Versions come from git tags (MinVer). Only the maintainer releases.

1. Merge the changes to `master`.
2. Update `CHANGELOG.md`: add a `## [X.Y.Z]` section (for example `## [0.1.0-preview.1]`) for the version. The pipeline refuses a tag
   without it, and uses that section as the GitHub Release notes.
3. Tag the merge commit `vX.Y.Z` or `vX.Y.Z-preview.N` and push the tag.
4. The release workflow validates, runs the full CI, packs and checks the packages, and then waits for approval in the
   `nuget-release` environment. Approve it to publish to nuget.org. After publishing it creates the GitHub Release and runs a
   smoke test against the published packages.

To rehearse without publishing, run the release workflow manually (`workflow_dispatch`) with `dry_run=true`.

If the smoke test fails after publishing: unlist the bad version (`dotnet nuget delete <id> <version>` unlists on nuget.org),
fix the problem, and tag the next patch or preview number. Never re-tag a version that was pushed.
