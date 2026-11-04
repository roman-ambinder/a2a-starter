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

- **No `InternalsVisibleTo` between shipped assemblies.** It is allowed only to the matching `*.Tests` assembly, to
  `Ambinder.A2A.Benchmarks` and to `Ambinder.A2A.Integration.Tests` (owner decision 2026-11-03); none of them is shipped. Share small
  helpers by linking source files from `src/Shared/` instead.
- **New dependencies are visible in review.** Add every new package dependency of a shipped project to
  `eng/package-dependencies.json` in the same PR. The package check fails when the packed dependencies differ from that file.
  Run it locally: `dotnet pack Ambinder.A2A.slnx -c Release -o artifacts/packages`, then
  `dotnet run eng/check-packages.cs -- --packages artifacts/packages --map eng/package-dependencies.json`. Core packages (`Protocol`, `Server`, `Client`) must not depend on
  ASP.NET Core, gRPC, Azure, EF or a database.
  `eng/check-core-only.sh` publishes `tests/Ambinder.A2A.CoreOnly.Smoke` (Server and Client only), fails when the output holds
  ASP.NET Core, gRPC, protobuf or Azure files, and runs the app.
- **Public API.** See the next section.
- **Performance rules.** Added by package 14.

## Public API

The public surface of each shipped project is tracked in `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` next to its `.csproj`
(Microsoft.CodeAnalysis.PublicApiAnalyzers). The rules are in the [versioning policy](docs/policies/versioning.md).

**Workflow when you change a public member**

1. Write the code and build. The build fails with RS0016 (member not declared) or RS0017 (declared member missing).
2. Add the lines with the analyzer's fix, **once per project**. Running it on `Ambinder.A2A.slnx` fixes only one project, so run it for each project you changed:
   ```bash
   dotnet format analyzers src/Ambinder.A2A.Server/Ambinder.A2A.Server.csproj --diagnostics RS0016 RS0037 --severity info
   ```
   For a removed or changed member, edit the old line in `PublicAPI.Unshipped.txt` by hand.
3. Read the diff of the API files. Every line must be something you meant to expose.
4. Commit the API files with the code. Never regenerate an API file wholesale: the diff is the review surface, and a pull request check (`public-api-diff`) shows it.

**During previews** every line stays in `PublicAPI.Unshipped.txt`; nothing moves to `Shipped` until `1.0.0-rc.1`. A removal or a change of a
public member during previews needs a line under `### Breaking changes` in `CHANGELOG.md`. After `rc.1` a removal needs a `*REMOVED*` line and the deprecation policy.

**Evolution rules in short** (full text in the [policy](docs/policies/versioning.md))

- No new abstract members on implementer-facing or mocked interfaces after `1.0.0`: use a default interface method or a new interface.
- Classes are `sealed` unless inheritance is a designed extension point. Models are `sealed record` with `init` properties and no positional constructor.
- No new optional parameters on an existing method: add an overload.
- Options are `sealed`, validated at startup, and the failure message names the property, the bad value and the fix.
- A record that can hold a secret overrides `PrintMembers` and has a `ToString()` test.
- A configuration key, telemetry name, header name, error code or log EventId is API.

**XML docs.** Every public and protected member has a doc comment (a missing one is CS1591, an error). `XmlDocQualityTests` checks the shape
(summaries, `<param>`, `<returns>`, examples on the entry points). Write what a caller needs to know, not what the name already says.

**Banned APIs and analyzers.** `eng/BannedSymbols.txt` lists APIs that shipped code must not call (a call to one fails the build, RS0030); each entry says what to use instead.
A suppression needs a `#pragma` with the reason next to it. `src/` builds with `AnalysisMode=Recommended`, `ConfigureAwait(false)` on every await (CA2007)
and `[LoggerMessage]` for every log message (CA1848, CA2254).

**Log EventIds** are API. Each assembly owns a block (Server 1000 to 1999, AspNetCore 2000 to 2999, AspNetCore.Grpc 3000 to 3999, Client 4000, Client.Grpc 5000,
Client.Entra 6000). Never change or reuse an id. The table is in the [policy](docs/policies/versioning.md#8-log-eventid-blocks); `LogEventIdTests` enforces it.

The pull request template has an "API files reviewed" checkbox, and `.github/CODEOWNERS` routes `PublicAPI.*.txt` to the API owner.

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
