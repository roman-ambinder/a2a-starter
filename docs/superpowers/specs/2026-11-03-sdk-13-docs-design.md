# SDK 13: Docs for product teams

Date: 2026-11-03
Status: draft for approval
Milestone: M2 (skeleton, getting started, package READMEs and the Phase 1 migration page already in M1)
Size: M

## Intent

Product teams will judge this SDK by its docs before they read its code. Today the docs explain this repo, not the SDK: a README about "Phase 1 mock services", `docs/flows.md`, `docs/architecture.html`, and `docs/production.md` as a list of things "not implemented" (R42).

This package builds the consumer docs: a site with a 5-minute start, guides for building agents and calling agents, an operations guide for AKS, a security guide, a migration guide from this repo's Phase 1 API, and a sample index. It also sets the rules that keep the docs true: code in guides comes from compiled samples, references are generated from code, and pages carry a "last verified" stamp.

Each package spec adds its own section. This spec owns the structure, the page rules, the build and the checks.

## Findings addressed

- R42: missing consumer-facing documentation. Install quickstarts, API reference site, versioning and support policy, store and bus guides, AKS guide, security and threat-model doc, verified Entra runbook, migration guide, troubleshooting and FAQ, SECURITY.md and CONTRIBUTING.md (the files themselves are created by package 01; this spec fills their content rules).
- R41 (part): the README framing "Phase 1 ... mock services" (`README.md:6`).
- R5 (docs side): the API reference needs complete XML docs (package 02 makes them; this spec renders them).
- Pain points: "Spec/sample quality that misleads integrators" (#24), "0.3 -> 1.0 breaking changes" (#1, the 0.3 page), "Wait for completion / long-running tasks" (#9), "Agent Card fetching, discovery" (#4, ingress), "Persistent task store, multi-instance, push" (#12), "Client auth" (#14), "Testing and mocking agents" (#21), "DI/host integration" (#22).

## Goals

1. A docs site built with DocFX, published per version (`/latest`, `/next`, and one folder per released minor), with search, API reference per package, and a link to the matching GitHub tag.
2. "Agent in 5 minutes" and "Call an agent in 5 minutes" pages. A person who has only the .NET 10 SDK installed finishes each in 5 minutes, measured (see Acceptance).
3. A guide section for building agents, one for calling agents, one for operations on AKS, one for security. Every package ships its pages in these sections (page template below).
4. A migration page from the Phase 1 API of this repo, backed by a machine-checked map: every public member of the `phase-1` tag is either in the new API or has a row saying where it went or why it is gone.
5. A sample index that lists every sample with what it shows, which packages it uses, and the command to run it. A test fails when a sample directory is missing from it.
6. Every code block longer than 5 lines in a guide is included from a compiled project in `samples/`. A check fails otherwise.
7. Reference pages that can drift are generated: options and defaults, error catalog, telemetry names, package list. A test fails when code and page disagree.
8. Every page in the operations and security sections shows "Last verified: version, date, environment". A CI warning appears when it is older than two minor releases.
9. The docs build has zero warnings: no broken link, no unresolved `xref`, no missing include.
10. The root README is rewritten for the SDK: what it is, the package table, the 20-line agent, the 5-line client, links. No "Phase 1" framing.
11. Per-package READMEs (they are the NuGet readme, package 01) follow one template and are tested for their required headings.
12. Page language is plain, short English. A style check (Vale, warning level) runs on every PR.

## Non-goals

- Writing the final technical content of other packages' pages: each package owns its pages (store guide: 05; cluster: 06; Entra: 07; push: 08; hosting and AKS manifests: 09; client: 10; authoring and AI: 11; testing: 12; 0.3 interop: 15). This spec fixes the places, the template and the checks.
- Making the AKS manifests themselves: package 09 (Kustomize manifests in `deploy/aks/`; 09 ships no Helm chart in v1). This spec requires them to be included by the guide and verified.
- The versioning policy text: package 02 writes it; this spec publishes it under Policies.
- The "known deviations and choices" content: package 03; this spec hosts the page and checks that every choice links to a spec section.
- Hosting the site on a custom domain, analytics, or a blog. GitHub Pages on the repo is enough for v1.
- Translations.
- `dotnet new` templates and a CLI. Not in v1.
- Video or interactive tutorials.

## Anti-goals

- A docs site that shows code nobody compiles.
- Pages that explain the A2A spec again. Link to the spec; say what the SDK does with it.
- A getting-started guide that needs a cloud account, Docker, or a secret.
- Guides that say "Phase 2" or "not implemented". If a feature is not there, the page says so once, with the package that will add it, or the page does not exist.
- A docs build that only works on the author's machine.
- Docs that describe the code as the author wished it were. Every claim about behavior has a test or a "verified on" stamp.

## Constraints

- Principle 3: "A working agent in about 20 lines and a client call in about 5", "Safe defaults, with nothing to configure for local development", "A getting started that works in 5 minutes", "Every error says what to do".
- Packages and their IDs: the map in package 01 (`Ambinder.A2A.AspNetCore` for a server, `Ambinder.A2A.Client` for a client; gRPC and Entra are opt-in packages).
- Preview packages need `--prerelease` (or an exact `--version`) with `dotnet add package`. Install commands in the docs are filled in with the right version at build time (`{{version}}` placeholder).
- .NET 10 only. The pinned SDK is in `global.json`.
- The existing test `tests/A2A.Integration.Tests/Repo/DocsTests.cs` checks README flow-map names and relative links in `README.md`, `docs/*.md`, `src/**/README.md` and `samples/**/README.md`. It keeps working after the rename (package 01 updates the solution file name it looks for) and is widened by this spec.
- Existing material to reuse: `docs/flows.md` (12 flows with diagrams), `docs/architecture.html`, the Observability section of `docs/production.md` (section 6), and the per-project READMEs in `src/` and `samples/`.
- One builder, no fixed dates, no second human reviewer (owner decision O2). The docs gates are automated (`docs` CI job); the owner approves releases.
- No cloud resources for now (owner decision O3): no test subscription, AKS cluster or Entra tenant. Live checks are written but run only when their secrets exist. Until then pages are verified with `kind`, fakes and unit tests, plus the manual pre-release checklist.
- A2A v1.0.1 terms: use the spec's names (agent card, task, message, part, artifact, context). Do not invent synonyms.

## Design

### 1. Site structure

Source lives in `docs/` (Markdown) and is built by DocFX (current stable 2.x) into `artifacts/docs/`. Folder = URL.

```
docs/
  index.md                      landing: what it is, pick your path (build an agent / call an agent / run on AKS)
  getting-started/
    agent-in-5-minutes.md
    call-an-agent-in-5-minutes.md
    packages.md                 the package map in plain words: "I want X, install Y"
  concepts/
    a2a-in-10-minutes.md        task, message, part, artifact, context; blocking vs stream vs push (decision table)
    how-the-sdk-fits.md         the package diagram; where each seam is (replaces architecture.html)
    flows.md                    the 12 flows (moved from docs/flows.md)
  agents/                       build an agent  (11, 05, 08, 03; middleware.md: 09)
    write-an-agent.md  task-lifecycle.md  input-and-auth-required.md  streaming-and-artifacts.md
    llm-agents.md  call-other-agents.md  extensions.md  stores.md  push-notifications.md
    multi-tenant.md  middleware.md  test-your-agent.md
  clients/                      call an agent  (10, 08, 15)
    discover-and-call.md  streaming.md  wait-for-completion.md  authentication.md
    resilience-and-timeouts.md  receive-webhooks.md  agent-as-ai-tool.md  talk-to-0.3-agents.md
    test-your-client.md
  operations/
    aks/                        (09, 06, 05)
      overview.md  deploy.md  ingress-sse-grpc.md  workload-identity.md  probes-and-shutdown.md
      scale-out.md  stores-in-production.md  observability.md  limits-and-rate-limits.md  runbooks.md
  security/                     (07, 04, 08)
    threat-model.md  identity-and-ownership.md  tenants.md  entra-setup.md
    push-and-ssrf.md  agent-card-trust.md  secrets-and-logging.md  hardening-checklist.md
  reference/
    api/                        DocFX output, one section per package
    configuration.md            generated
    errors.md                   generated skeleton + hand-written "what to do"
    telemetry.md                generated names + hand-written meaning
    wire-and-spec-choices.md    from package 03 ("known deviations and choices")
    packages.md                 generated from the pack output
  migration/
    from-phase-1.md  phase1-map.csv  coexist-with-official-a2a-dotnet.md  from-0.3.md
  samples/index.md              generated from sample front matter
  policies/
    versioning.md  support.md  security-reporting.md (from SECURITY.md)  contributing.md (from CONTRIBUTING.md)
  troubleshooting.md            symptom -> cause -> fix; top 20, grows with issues
  faq.md
```

M1 subset (so early teams are not blocked): `index`, `getting-started/*`, `concepts/a2a-in-10-minutes`, `concepts/how-the-sdk-fits`, `migration/from-phase-1` and its map, `policies/versioning`, API reference, package READMEs, rewritten root README. Everything else lands by `1.0.0`. The existing `docs/production.md` is removed once its content has a new home; its observability part moves to `operations/aks/observability.md` and `reference/telemetry.md`, its stale "Phase 2 - not implemented" parts are dropped (they are replaced by the real pages of packages 05 to 09). Moving files is one PR, and `DocsTests` is updated in the same PR.

### 2. Page rules

Every guide page follows this shape *(writer's call)*:

1. **Title** as an action: "Stream results to the caller".
2. **One-line goal** and "Time: N minutes" for getting-started pages.
3. **Before you start**: packages (with the install command) and what must already work.
4. **Steps**: at most 7, each one action. Code first, explanation after.
5. **Check it worked**: the exact output or request to expect.
6. **If it fails**: the 2 to 3 most likely errors, each as "message -> cause -> fix". Error messages quoted here are copied from the real error text (a test searches the source for the quoted text, so a changed message breaks the doc build).
7. **Next**: one or two links.
8. **Front matter**: `packages`, `since` (version), `verified` (version, date, environment; required for operations and security pages), `owner` (package number).

Length: keep a page under about 800 words; split when longer. Sentences short, words plain. Define a term at its first use on the page, link to `concepts/` for more. Never repeat the spec; link to the section number in the A2A spec.

Code rules:

- Any C# block over 5 lines is a DocFX include (`[!code-csharp[](../../samples/GettingStarted.Agent/Program.cs#hello)]`) from a project in `samples/` that builds in CI. Short fragments (up to 5 lines), shell commands, JSON, YAML and tables may be inline. A markdown check (`eng/DocsLint`) fails on a long inline C# block, except when preceded by `<!-- illustrative -->`, and it counts those so the number does not grow.
- Getting-started code is a whole file, copy-paste complete, with `using`s.
- Config is shown in `appsettings.json` form and as an environment variable.
- Every shell block says which OS it was run on if it differs between Windows and Linux.

### 3. The 5-minute pages

**Agent in 5 minutes** (sample `samples/GettingStarted.Agent`, about 20 lines; its code is owned by package 11):

1. `dotnet new web -n HelloAgent` then `cd HelloAgent`.
2. `dotnet add package Ambinder.A2A.AspNetCore --prerelease` (exact version filled in at build).
3. Replace `Program.cs` with the sample (about 20 lines: one agent class that echoes, `AddA2AServer`, `AddAgent`, `MapA2A`).
4. `dotnet run`.
5. Check: `curl http://localhost:5000/.well-known/agent-card.json` shows the card; then the SDK's own call from the next page.
6. If it fails: port already in use, missing `A2A-Version` header on a raw request (the 400 text says what to send), wrong `--prerelease`.

**Call an agent in 5 minutes** (sample `samples/GettingStarted.Client`, about 5 lines of calls; owned by package 10):

1. `dotnet new console -n HelloClient`, `dotnet add package Ambinder.A2A.Client --prerelease`.
2. Paste the sample: create the client from the agent URL, send one text message, print the reply.
3. Run it while the agent from the previous page is running.
4. Check output; then a streaming variant in the same file.
5. If it fails: connection refused, 401 (agent has auth: link to client auth), no supported interface (name the package to add).

No Docker, no account, no secret, no API key (safe defaults for local development, principle 3). The agent works with no LLM. A short "next" box points to the LLM agent page.

### 4. Content each section must cover

The list below is the minimum. The owner package writes the page; 13 reviews it against the template.

**Build an agent (`agents/`)**

- `write-an-agent`: the handler, replying with a message vs working with a task, what the framework does for you (IDs, state rules, history), the per-execution instance and why `HttpContext` is not there.
- `task-lifecycle`: states, which end the stream, `INPUT_REQUIRED` and `AUTH_REQUIRED` in plain words, what a cancel does.
- `llm-agents`: Microsoft.Extensions.AI agent, streaming deltas to artifacts, chat history from the context (11).
- `stores`: in-memory default, switching to EF Core (PostgreSQL first, SQLite for local) or Cosmos DB; migrations; the conformance suite for people who write their own store (05).
- `push-notifications`: when to use push instead of streaming, SSRF rules, delivery guarantees (08).
- `test-your-agent`: the testing package (12).
- `multi-tenant`: how the tenant is resolved and enforced (07).

**Call an agent (`clients/`)**

- `discover-and-call`: card fetch, interface choice, one call.
- `wait-for-completion`: send and wait, poll vs subscribe, input-required loops (pain point #9).
- `authentication`: Entra with workload identity on AKS, client secret for local, on-behalf-of, 401 retry (07, 10).
- `resilience-and-timeouts`: what is retried, and why `SendMessage` is retried only with `AllowSendRetry`, only for new-task sends, and only against servers that dedupe by `messageId` (the rule in 05 §7.10 and 10 "Resilience"); timeouts for long tasks (10).
- `receive-webhooks`: the receiver package, verifying the signature and the task id (08).
- `agent-as-ai-tool`: call a remote agent from an LLM as a function (11).
- `talk-to-0.3-agents`: how version negotiation works, what does not bridge (`security` fields), how to see which version was used (15).

**Operations on AKS (`operations/aks/`)** (the section R42 asks for)

- `overview`: a one-page picture of a production deployment and the list of what must be set (public URL, store, auth, signing key, OTLP endpoint). The SDK does not ship a collector: the page points to `deploy/aks/overlays/otel/` (package 09), which sets `OTEL_EXPORTER_OTLP_ENDPOINT` to a collector the team runs.
- `deploy`: the Kustomize manifests and overlays from `deploy/aks/`, resource requests, the environment variables for each option.
- `ingress-sse-grpc`: idle timeouts for SSE (the ingress default is 60 seconds, R24) and the heartbeat setting, buffering off, HTTP/2 and gRPC through ingress, the card URL when TLS ends at the ingress (R21, R25). The builder checks the current state of each ingress option at writing time (ingress-nginx's maintenance status in particular) and documents only supported ones.
- `workload-identity`: the steps from a federated credential to a token for the agent. Verified against a real tenant once one exists (O3); until then stamped `unit-tests` with the note "not yet verified on a real tenant".
- `probes-and-shutdown`: liveness and readiness endpoints, `terminationGracePeriodSeconds`, what happens to running tasks on SIGTERM (R22).
- `scale-out`: until package 06 ships (M2), this page says only "do not scale out before 1.0": M1 runs one replica with `strategy: Recreate` (09, B1). From M2: replicas, owner-pod forwarding, pod disruption budget, what the store must provide (EF Core only in 1.0, B3) (06).
- `stores-in-production`: connection settings, migrations, retention, backups.
- `observability`: the OTLP setup from the Observability section of `docs/production.md` (section 6 there), the dashboards and alerts worth setting, the metric names.
- `limits-and-rate-limits`: body size, concurrency, rate limiting.
- `runbooks`: stuck task, store outage, push backlog, pod loss, key rotation. Each runbook is "symptom, check, fix".

**Security (`security/`)**

- `threat-model`: assets, trust boundaries, who can do what, the attacks the SDK defends against and the ones it leaves to the host. One diagram.
- `identity-and-ownership`: how the owner is derived (app-only: tenant + app; delegated: tenant + user), what `AUTH_REQUIRED` continuation allows, how to replace the resolver (07).
- `entra-setup`: a step-by-step runbook for app registrations, app roles and consent. It is the runbook package 07's `entra-live` job uses. It is verified against a real tenant when that job can run (O3); until then it is stamped `unit-tests` (fake authority) and the manual pre-release checklist covers the steps.
- `push-and-ssrf`, `agent-card-trust` (signing and verification, what a failed check means, 04), `secrets-and-logging` (what is never logged, how to supply keys from Key Vault), `hardening-checklist` (one page, checkboxes, each linking to the page that explains it).

### 5. Phase 1 migration (machine-checked)

1. Tag the last Phase 1 commit `phase-1` (package 01 does it before the rename).
2. A one-time tool run at that tag writes `docs/migration/phase1-public-api.txt`: every public type and member, one per line. (Use the same PublicApiAnalyzers file format as package 02: building the Phase 1 projects with the analyzer and taking `PublicAPI.Unshipped.txt` is the simplest way. The builder confirms.) This file is committed and never edited.
3. `docs/migration/phase1-map.csv` has one row per Phase 1 symbol group: `old symbol, new symbol, package, change` where change is one of `renamed`, `moved`, `now internal`, `replaced by <X>`, `removed: <reason>`. Rows can cover a whole type or a whole namespace.
4. `MigrationMapTests` fails when a Phase 1 symbol is neither in the current API files (by new name via the map) nor in the map. A PR that removes or changes a Phase 1 public member must add a row. This is the only check that keeps the guide complete as packages land.
5. `from-phase-1.md` explains in order: (a) install the new packages (a table: old project -> new package, from package 01); (b) find and replace the namespace `A2A.` to `Ambinder.A2A.`; (c) the behavior changes that need a decision, each with before, after and what to do: telemetry source and meter names renamed, task owner key changed (package 07), the `A2A-Version` header rules (package 03), `ConfigureA2AKestrel` and gRPC-by-port replaced (package 09), store contract changed (package 05), `AgentRequestContext` and sink changes (package 11), transport clients no longer public (package 10), push signing header and key (package 08); (d) a list of config keys (these stay under `A2A:`).
6. `coexist-with-official-a2a-dotnet.md` is short: the two SDKs can be referenced together, how to alias namespaces, which names collide, and a map of main concepts (`A2AClient` there, `IA2AClient` here, and so on). It does not say one is better; it says what each ships (facts from the SDK research: in-memory store only, preview). *(writer's call; the brief names only the Phase 1 migration, but the clash with the official SDK is the first reader question)*
7. `from-0.3.md` (package 15 writes it) links the Python/JS migration guides for peers, lists the wire differences with the spec's "what's new" document as source.

### 6. Sample index

- Each sample directory has a `README.md` with front matter: `title`, `shows` (one sentence), `packages` (list), `run` (the command), `needs` (docker, tenant, or "nothing"), `lines` (approximate size of the main file).
- `samples/index.md` is generated from them by `eng/DocsLint`. A test fails when a `samples/*` directory has no front matter or is missing from the index.
- Existing samples: `AgentServer` (four agents), `AgentClient` (all flows), `MockEntra`, `WebhookReceiver`, `AppHost`, `SampleTelemetry` (shared helper, listed as "support"). New ones named in this plan: `GettingStarted.Agent`, `GettingStarted.Client`, an LLM agent (11), a supervisor agent that calls others as tools (11), a multi-instance AKS sample (06, 09), a store switch sample (05), a webhook receiver (08), a test-your-agent sample (12), a native AOT sample (01).
- Rule: samples named `GettingStarted.*` and the ones included in guides build against the packed `.nupkg` files (package 01's local-feed job), so what the docs show is what the packages do. Other samples may use project references.
- Hello-world samples carry no auth and no LLM. Samples that need Entra say so in `needs`, and MockEntra is the local stand-in (never in production, as today).

### 7. Generated reference

`eng/DocsLint` (a small console tool in the repo; the builder may split it) produces or checks:

| Page | Source | Check |
|---|---|---|
| `reference/configuration.md` | every `*Options` type: property, type, default, description | defaults are read by creating the options object, so they cannot drift; the description is the property's XML doc; a test fails when an options property has no doc |
| `reference/errors.md` | `A2AErrorCode` and `A2AError` | one hand-written "what to do" entry per code; a test fails when a code has no entry. Fields: JSON-RPC code, HTTP status, gRPC status, `ErrorInfo` reason, meaning, what to do |
| `reference/telemetry.md` | constants of `A2ATelemetryNames` | each name has a meaning row; unknown name fails the test. Includes the "never recorded" list from the Phase 1 doc |
| `reference/packages.md` | the pack output | package id, description, dependencies, size |
| `samples/index.md` | sample front matter | see above |
| API reference | DocFX metadata from the built assemblies and their `.xml` files (package 02) | `xref` warnings are errors |

### 8. Build, check and publish

- `docfx.json` at the repo root; `toc.yml` per section; the API section groups namespaces by package.
- CI job `docs` runs on every PR that touches `docs/`, `samples/` or `src/**/README.md` and on `master`: build with warnings as errors, run `DocsLint`, run `DocsTests` (links, flow map, sample index, migration map, quoted error messages, generated pages up to date), run Vale (warning level; rules: sentence length over 25 words, a short list of banned fillers and jargon words, passive voice as a hint).
- `{{version}}` and `{{prerelease}}` placeholders are replaced from MinVer at build, so an install command is always the real one.
- On a release tag, the `docs` hook job in package 01's release pipeline builds the site and deploys it to GitHub Pages in `/v<major>.<minor>/`, updates `/latest` (stable only) and the version switcher. `master` publishes to `/next/` on every push. Pre-release tags publish to `/next/` only.
- The site shows a banner on `/next/` and on a version that is not the newest: "This is the docs for a pre-release / older version".
- Link checking of external URLs runs weekly (not on PRs), opens an issue on a failure.

### 9. Verification stamps

Operations and security pages have `verified: { version, date, env }` in front matter. `env` is `aks-<k8s version>` or `kind` or `unit-tests`. A page may only claim `aks-...` after a person ran its steps on AKS. With no cloud resources for now (O3), pages claim `kind` or `unit-tests`; that is enough for a release, and the page shows the note "not yet verified on AKS" or "not yet verified on a real tenant". `DocsLint` warns when `version` is more than two minors behind the current one and fails the release build for a stable release when any page in these two sections has no stamp. The owner package's acceptance includes the stamp. Package 09 also runs its manifests on a `kind` cluster in CI (cheap, catches YAML drift) but that does not replace the AKS stamp.

### 10. Root README and package READMEs

Root `README.md` (rewrite; `DocsTests` flow-map section kept or moved to `docs/concepts/flows.md` with the test updated):

1. One paragraph: what A2A is, what this SDK gives.
2. The package table (id, one line, install command), generated from the package map.
3. The 20-line agent and the 5-line client (included from the samples).
4. "Run on AKS" and "Secure it" links.
5. Status: version and spec version (A2A v1.0.1), license, link to policies.

Package README template (package 01 ships it in the nupkg):

`# <Package id>`, one-line purpose, **Install** (with `--prerelease` where needed), **Use** (at most 15 lines), **Configure** (the 3 options most people touch; link to the generated reference), **Extend** (the seams, one line each), **Docs** (link to the page), **Requires** (.NET 10, dependent packages). A test checks the headings and that the install command names the package.

## How it meets the six principles

1. **Protocol.** The docs host the "wire and spec choices" page, where every choice cites a spec section (package 03). Check: each entry has a section number (lint). The flows page keeps one diagram per flow, and `DocsTests` keeps it in sync with the flow tests.
2. **.NET enabler.** The reference is generated from XML docs (package 02); guides use `appsettings.json`, `IOptions`, `ILogger`, OpenTelemetry the usual way. Check: the options reference is generated, not typed.
3. **Easy.** The two 5-minute pages are the product. Check: (a) the post-publish smoke test (package 01) runs their exact steps against nuget.org on every release; (b) before `1.0.0`, the owner (who did not write the SDK code; there is no second reviewer, O2) follows them on a clean machine with a timer: under 5 minutes each, with no help, or the page is fixed and the run repeated; (c) agent sample is 20 lines or fewer, client calls are 5 lines or fewer, counted by a test on the included region.
4. **Performance.** The docs include a "Performance and sizing" page produced from the nightly benchmark results (package 14): latest numbers with the hardware named, plus the tuning knobs (limits, heartbeats, store choices). Check: the page is generated from the benchmark JSON, not typed.
5. **Modular and extendable.** `getting-started/packages.md` answers "I want X, install Y" and shows the dependency rule (core has no web host). Each seam has a how-to page: store (05), event bus and cluster (06), owner resolver (07), push sender and key provider (08), server middleware (09), extensions (11), client interceptors (10). Check: every public interface in the "implementer-facing" list (package 02) is linked from at least one guide (lint on the API files).
6. **Integration pains.** One page per pain point in the research, titled by the symptom: waiting for a long task, SSE dropping behind ingress, card shows the internal port, 0.3 peers, Entra on AKS, webhook verification, testing without a network, running two SDKs side by side. Check: `troubleshooting.md` starts with these and each row links to the guide that fixes it. Pain-point coverage table in this spec's PR: every "A" and "B" pain point in `research-painpoints.md` that the work plan takes on has a page or a section (reviewed once before `1.0.0`).

## Dependencies

- **Needs from 01:** package IDs and map, the `phase-1` tag, the docs hook in `release.yml`, the local-feed sample job, CONTRIBUTING and SECURITY files, the README template location.
- **Needs from 02:** complete XML docs; API files (the Phase 1 map check uses them); versioning policy text.
- **Needs from 03:** the "wire and spec choices" content. **From 14:** nightly benchmark JSON for the performance page.
- **Needs from every other package:** its pages (template above), its samples, its stamp. Packages 05 to 12 and 15 each list their pages in their own spec's acceptance criteria.
- **Provides:** the folder and page template, `eng/DocsLint`, `MigrationMapTests`, `DocsTests` (widened), the `docs` CI job, the Pages deployment, the generated reference pages, the sample index.
- **Types and files this package creates:** `eng/DocsLint`, `docfx.json`, `docs/migration/phase1-public-api.txt`, `docs/migration/phase1-map.csv`, test classes `MigrationMapTests`, `DocsTests` (extended), `ReadmeTemplateTests`.

## Acceptance criteria

- [ ] `docfx build` succeeds with zero warnings in CI; the site deploys to `/next/` from `master` and to `/v<major>.<minor>/` plus `/latest` on a stable tag (tested on a fork or with a dry run).
- [ ] `getting-started/agent-in-5-minutes` and `call-an-agent-in-5-minutes` exist; the owner's timed run (O2) is under 5 minutes each (result and date recorded in the PR); the agent sample is 20 lines or fewer and the client calls 5 lines or fewer (test).
- [ ] The post-publish smoke test of package 01 runs the code of these two pages.
- [ ] Every page in `agents/`, `clients/`, `operations/aks/` and `security/` listed in section 1 exists, follows the template, and has front matter. Pages in `operations/` and `security/` have a `verified` stamp. The Entra and workload-identity pages are stamped `aks-...` with a real tenant when one exists; until then (O3) they are stamped `unit-tests` or `kind` with the "not yet verified" note, and the manual pre-release checklist lists their steps.
- [ ] `MigrationMapTests` passes: every symbol in `phase1-public-api.txt` is mapped or present. The guide `from-phase-1.md` covers all behavior changes in section 5.
- [ ] `DocsLint` passes: long inline C# blocks (outside the counted `illustrative` ones), quoted error messages found in source, options and error and telemetry pages current, sample index complete.
- [ ] `DocsTests` (links, flow map) pass over the new tree, including `docs/**/*.md`.
- [ ] Root README and all package READMEs are rewritten; `ReadmeTemplateTests` passes; no file mentions "Phase 1" or "Phase 2" outside `migration/` and the history specs.
- [ ] `policies/versioning.md` is published; `SECURITY.md` and `CONTRIBUTING.md` are linked in the site.
- [ ] `troubleshooting.md` has an entry for each pain-point page listed in principle 6, each linking to a guide.
- [ ] The performance page is generated from the latest benchmark artifacts.
- [ ] The Vale job runs and its warning count is reported; it is not a gate in M1, and a gate (no new warnings) at `1.0.0`.

## Left to the builder

- DocFX template and theme, search, version switcher implementation.
- Whether `DocsLint` is one tool or several, and its language.
- Diagram format (Mermaid in Markdown is fine; the existing `docs/flows.md` diagrams show what works).
- The exact Vale rule list and word list.
- Page order inside sections, and wording.
- How the timed 5-minute run is recorded.
- Whether `docs/architecture.html` is converted to a Mermaid page or dropped.

## Open questions

None. The verification environment (Q1) is owner decision O3: no cloud resources for now. Pages are stamped `kind` or `unit-tests` until live resources exist (section 9).
