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
  `eng/check-core-only.sh` publishes `tests/Ambinder.A2A.CoreOnly.Smoke` (Server and Client only), fails when the output holds
  ASP.NET Core, gRPC, protobuf or Azure files, and runs the app.
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

Versions come from git tags (MinVer), start at `1.0.0` previews and are the same for all packages. Only the maintainer releases.

1. Merge the changes to `master`.
2. Update `CHANGELOG.md`: add a `## [X.Y.Z]` section (for example `## [1.0.0-preview.1]` for the tag `v1.0.0-preview.1`) for the
   version. The pipeline refuses a tag without it, and uses that section as the GitHub Release notes.
3. Tag the merge commit `vX.Y.Z` or `vX.Y.Z-preview.N` and push the tag.
4. The `Release` workflow (`.github/workflows/release.yml`) runs these jobs in order:
   - `validate`: the tag is valid SemVer, its commit is on `master`, and `CHANGELOG.md` has the section.
   - `verify`: the full CI workflow on the tagged commit, with Low and Moderate NuGet advisories as errors, the Windows leg and
     the TCK, interop and benchmark hooks.
   - `pack`: packs with `ContinuousIntegrationBuild`, checks that the packages carry the tag's version, runs
     `eng/check-packages.cs` and `sourcelink test` on every symbol file.
   - `sign`: signs the `.nupkg` files with Azure Trusted Signing when the repository variable `SIGN_PACKAGES` is `true`.
     Without it a preview ships unsigned (a notice in the log); a stable version (no `-` in the version) fails here.
   - `attest`: CycloneDX SBOM and a build-provenance attestation of the packages.
   - `publish`: waits for your approval in the `nuget-release` environment, then pushes the packages and symbols to nuget.org
     (`--skip-duplicate`). Approve it only after the earlier jobs are green: a pushed version can be unlisted but never replaced.
   - `github-release`: creates the GitHub Release with the `CHANGELOG.md` section as notes (marked pre-release for versions with a
     `-`) and attaches the packages, the SBOM and the attestation bundle.
   - `smoke`: waits until nuget.org lists the version (up to 20 minutes), then installs `Ambinder.A2A.AspNetCore` and
     `Ambinder.A2A.Client` at exactly that version into fresh projects, runs `samples/GettingStarted.Agent` and calls it with
     `samples/GettingStarted.Client` (`eng/smoke-test.sh`). It opens an issue on failure.
   - `docs`: hook for the versioned docs site (package 13).

### Rehearsal (dry run)

Run the `Release` workflow by hand (Actions, Release, Run workflow) with "Use workflow from" set to an existing tag, the input
`tag` set to the same tag, and `dry_run` on (the default). Every job runs except `publish` and `github-release`; `smoke-local` runs the
smoke test against the packages built in the run instead of nuget.org. Nothing is published. The `sourcelink test` step downloads
sources from GitHub, so the repository must be public and the commit pushed.

### One-time setup (owner)

- Environment `nuget-release` with the owner as required reviewer.
- Repository variables (Settings, Secrets and variables, Actions, Variables):
  - `NUGET_USER`: the nuget.org account name for trusted publishing. Add a trusted-publishing policy for `Ambinder.*` on nuget.org.
    If that is not available, set `NUGET_USE_API_KEY` to `true` and store a scoped API key (glob `Ambinder.*`, push only, expiring)
    as the secret `NUGET_API_KEY` of the `nuget-release` environment. That secret is the only secret the workflows use, and only in
    this fallback.
  - `SIGN_PACKAGES`: `true` once Azure Trusted Signing is set up (required for any stable version), together with
    `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (an Entra app with a federated credential for this repository)
    and `TRUSTED_SIGNING_ENDPOINT`, `TRUSTED_SIGNING_ACCOUNT`, `TRUSTED_SIGNING_PROFILE` (the Trusted Signing endpoint URL, account
    name and certificate profile name).

### If something goes wrong

If the smoke test fails after publishing: unlist the bad version (`dotnet nuget delete <id> <version>` unlists on nuget.org),
fix the problem, and tag the next patch or preview number. Never re-tag a version that was pushed.
