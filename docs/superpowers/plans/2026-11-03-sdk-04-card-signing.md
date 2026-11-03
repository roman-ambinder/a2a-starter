# Package 04: Agent Card Signing and Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task, with every subagent on model `sonnet` (see [Execution notes](#execution-notes)). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The server signs every agent card it serves (public and extended) with JWS over the RFC 8785 (JCS) payload that spec 8.4 defines, and the client verifies cards under the policy `Off`, `IfPresent` (default) or `Required`. Every verify failure has a reason code. A separate `Ambinder.A2A.Azure.KeyVault` package signs inside Key Vault.

**Architecture:**
- `Ambinder.A2A.Protocol`, namespace `Ambinder.A2A.Protocol.Signing`. BCL only, no new package dependencies. It holds:
  - `JsonCanonicalizer` (RFC 8785).
  - `AgentCardCanonicalizer`, which applies the spec 8.4 field rules through a field table generated from `proto/a2a.proto`.
  - `CardJws` (sign and verify primitives).
  - The key types: `IAgentCardSigningKeySource`, `A2ASigningKey`, `JsonWebKeyPublic`, local keys.
- `Ambinder.A2A.Server` signs once per card version and key set. The B13 ETag (hash of the unsigned canonical payload plus the key ids) is cached with the signed card.
- `Ambinder.A2A.AspNetCore` serves the optional card JWKS endpoint.
- `Ambinder.A2A.Client` verifies the raw card bytes before deserializing them. It has `IAgentCardVerifier`, `CardVerificationOptions` and the policy, and throws `A2ACardVerificationException` on reject.
- `Ambinder.A2A.Azure.KeyVault` is a new package. It holds the Key Vault key source with `RefreshInterval`, `PublishLead` and `RetireGrace`. Package 08 also uses it for push signing.

**Tech Stack:**
- .NET 10 and C# `latest`.
- `System.Text.Json`, `System.Security.Cryptography`, `System.Buffers.Text.Base64Url`, `System.Collections.Frozen`.
- xUnit v3 with Microsoft.Testing.Platform.
- `Azure.Security.KeyVault.Keys` and `Azure.Identity`, in the Key Vault package only.

**Spec:** `docs/superpowers/specs/2026-11-03-sdk-04-card-signing-design.md`. It is binding, together with the work plan `docs/superpowers/specs/2026-11-03-sdk-00-work-plan.md`.

**Related specs:**
- 01: package map and naming rules.
- 03: ETag rule B13, `IAgentCardContributor`, `docs/spec-decisions.md`.
- 07: default Azure credential rule.
- 08: shared key source.
- 10: card resolver.
- 16: interop vectors.

---

## Assumption: package 01 has landed

This plan assumes package 01 (`2026-11-03-sdk-01-packaging-release-design.md`) is merged into `master`. That means:

- Every project, namespace and assembly is `Ambinder.A2A.*`.
- The old `A2A.Server` is split into `Ambinder.A2A.Server` (core, no web host) and `Ambinder.A2A.AspNetCore` (endpoints, `MapA2A`).
- `src/Directory.Build.props`, `NOTICE`, MinVer and the dependency allow-list (`eng/package-dependencies.json`) exist.

All file paths below use the post-01 names. If Task 0's check shows 01 has **not** landed, stop and report. Do not run this plan against the `A2A.*` names.

Steps that depend on package 01:

| Step | What it needs from 01 |
|---|---|
| Task 0 | the renamed solution and projects exist |
| every task | paths `src/Ambinder.A2A.{Protocol,Server,AspNetCore,Client}/`, namespaces `Ambinder.A2A.*`, test projects `tests/Ambinder.A2A.*.Tests/` |
| Task 9 | the split: card endpoints in `Ambinder.A2A.AspNetCore`, task manager in `Ambinder.A2A.Server` |
| Task 10 | `MapA2A` in `Ambinder.A2A.AspNetCore` |
| Task 14 | telemetry meter names follow assembly names (`Ambinder.A2A.Client`) |
| Task 16 | package ID `Ambinder.A2A.Azure.KeyVault` in the package map; `src/Directory.Build.props` packaging metadata; `IsAotCompatible=false` for it |
| Task 17 | the dependency allow-list test and `eng/package-dependencies.json` |
| Task 15, Task 2 | `NOTICE` (attribution for copied RFC 8785 test data) |

## Other dependencies: check, then branch

Package 04 depends on 03, 07, 10 and 16 (spec "Dependencies"). They may or may not have landed when this plan runs. Each affected task starts with a check command and says what to do in each case. Summary:

| Dependency | Used in | If landed | If not landed |
|---|---|---|---|
| 03 card pipeline (`IAgentCardContributor`, per-version card bytes cache) | Task 9 | Branch A: sign inside 03's cache, after the contributors run | Branch B: sign in the card endpoint's serializer; implement B13 there |
| 03 `docs/spec-decisions.md` | Task 19 | add rows | the file already exists on `master` today; add rows |
| 07 credential helper | Task 16 | reuse it only if it is public in a package that `Ambinder.A2A.Azure.KeyVault` may reference (it may reference `Ambinder.A2A.Server` and Azure packages only) | implement the same rule as an internal helper |
| 10 resolver (`IA2ACardResolver`, `ResolvedAgentCard`, `AddA2AClient`, `A2AClient`; plan `2026-11-03-sdk-10-client.md`) | Task 14 | Branch A: wire the verifier into 10's resolver, options and DI | Branch B: verify inside the existing `A2ACardResolver` only; 10's types and DI are left to 10, with a hand-off |
| 02 PublicAPI files | every task with public API | add lines to `PublicAPI.Unshipped.txt` | nothing |
| 14 benchmarks project | Task 18 | add benchmarks | record a hand-off in the PR body |
| 16 interop harness | Task 15 | consume `python/` and `js/` vectors | ship empty folders with README; the test skips visibly |

## Global constraints

- **No new dependencies in the core.** `Ambinder.A2A.Protocol` stays at zero package dependencies. `Server`, `AspNetCore` and `Client` get no Azure, IdentityModel or JOSE library. JWS is built by hand on `System.Security.Cryptography`.
- **Package versions:** only `Ambinder.A2A.Azure.KeyVault` adds packages: `Azure.Security.KeyVault.Keys` and `Azure.Identity`. `Azure.Identity` is already pinned in `Directory.Packages.props`. For `Azure.Security.KeyVault.Keys`, take the latest **stable** version on nuget.org at execution time and pin it centrally. Do not guess a version number. Look it up.
- **Build rules:** `TreatWarningsAsErrors`, nullable on, central package management, XML docs on every public member (package 02). Match the surrounding code style and comment density.
- **AOT:** Protocol, Server, AspNetCore and Client are `IsAotCompatible`. No reflection-based JSON. New JSON types go into `A2AJsonContext`, or use `JsonElement`/`JsonNode` with `Utf8JsonWriter`.
- **Package 02 rules** (`docs/superpowers/plans/2026-11-03-sdk-02-public-api.md`; 02 may land before or after this plan). They apply to every task:
  - **JSON metadata outside Protocol.** Code in `Server`, `AspNetCore`, `Client` and `Azure.KeyVault` never uses `A2AJsonContext` (02 makes it internal). Use the `A2AJson` typed accessors (`A2AJson.AgentCard`, and the `A2AJson.JsonWebKeySet` and `A2AJson.JsonWebKeyPublic` accessors that Task 5 adds). If 02's Task 2 has not landed, add those two accessors in Task 5 anyway, and use `(JsonTypeInfo<AgentCard>)A2AJson.Options.GetTypeInfo(typeof(AgentCard))` for the card. Do not serialize a served card with `A2AJsonContext.Default.AgentCard`: it lacks the "omit empty lists" modifier of `A2AJson.Options`, so the bytes (and the signing payload and ETag) would differ from today's served card. Tests that have `InternalsVisibleTo` to Protocol may still use `A2AJsonContext`.
  - **No `InternalsVisibleTo` between shipped assemblies** (spec 01 rule 6). The card endpoints in `Ambinder.A2A.AspNetCore` reach the signer only through the public `IAgentCardSigner` seam (P18). A pure helper that two shipped assemblies need (`AgentCardETag`) is a linked file in `src/Shared/`, compiled `internal` into each (spec 01 rule 10).
  - **No positional public records.** Public records use `required` / `init` properties (`JsonWebKeySet`, `CardVerificationResult`, `SignedAgentCard` below).
  - **Complete XML docs.** The code blocks in this plan leave out some XML docs to stay short. The implementation documents every public member (`<summary>`, `<param>`, `<returns>`, `<exception>`), puts `<remarks>` with lifetime and concurrency on every implementer-facing interface, states the default of every options property, and adds "More values can be added in later versions; handle unknown values." to every public enum. Once 02 has landed, a gap fails the build (CS1591) and `XmlDocQualityTests`.
  - **API files and records.** If `PublicAPI.Unshipped.txt` exists, update it with the analyzer fix in the task that adds the API (not only in Task 19). New public types must pass 02's `PublicSurfaceTests` allow-list: `Ambinder.A2A.Azure.KeyVault` may expose `Azure.Core` types (`TokenCredential`) but no `Azure.Security.KeyVault.*` type, so `KeyVaultSigningKey`, `IKeyVaultKeyOperations` and the adapter stay `internal`.
  - **Logging.** `[LoggerMessage]` with fixed `EventId`s in the assembly's block from the 02 plan (Task 7.3: `Server` 1000–1999, `AspNetCore` 2000–2999, `Client` 4000–4999), unless `docs/policies/versioning.md` already lists another scheme. `Ambinder.A2A.Azure.KeyVault` takes the next free block of 1000 and records it there.
- **Bytes are exact:** files under `tests/vectors/**` are compared byte for byte. Add `tests/vectors/** -text` to `.gitattributes`, so git never rewrites line endings. Payload files have no trailing newline.
- **Error messages say what to do** (principle 3). The message texts given in this plan are the required wording. Tests assert on key phrases.
- **Never log** private key material, full signatures or JWKS bodies. Log key ids, algorithm names, card URLs (without query string) and status codes.
- **Allowed algorithms:** ES256 (default), ES384, PS256, RS256. Never `none` and never HMAC. RSA keys must be at least 2048 bits.
- **TDD:** every task writes the failing test first, runs it to see it fail, implements, then runs it to see it pass. Commit after each task.
- **Commits:** one commit per task, message `feat(signing): <what>` (or `test(signing):`, `docs(signing):`). End each commit message with the attribution lines the session's system prompt gives. Never put a model name in a commit message.
- **Do not change** `proto/a2a.proto`.

## Decisions this plan makes (planner's calls, flagged for owner review in the PR)

The spec leaves these points open, or the plan needed one exact rule to be testable. Each one is also recorded in code comments, and in `docs/spec-decisions.md` where it touches the wire (Task 19).

- **P1 `KeyExpired` source.** `JsonWebKeyPublic` gets an extra init property `NotAfter` (`DateTimeOffset?`, `[JsonIgnore]`, never written to a JWKS). Only pinned `TrustedKeys` can carry it. `KeyExpired` means "the matching pinned key has `NotAfter` < now". JWKS documents have no expiry; the server removes keys from them instead.
- **P2 `Policy = Off` result.** The spec's 10 shape `ResolvedAgentCard(... CardVerificationResult Verification ...)` is non-null. So `CardVerificationStatus` gets one extra value, `NotChecked`, returned only when the policy is `Off`.
- **P3 published keys.** `IAgentCardSigningKeySource` gets a second member with a default body: `GetPublishedKeysAsync` (default: the public JWKs of `GetSigningKeysAsync`). The Key Vault source overrides it. This is the shape of 08's `IPushSigningKeySource`, and the spec requires that "the card JWKS and the push JWKS are both built from this list".
- **P4 variant canonicalizers live in Protocol** behind a public enum `CanonicalizationProfile { Spec, DropRequiredDefaults, Python112 }`. The spec names this enum for future TSC rulings. They cannot live in Client: they need the internal field table, and 01 rule 6 forbids `InternalsVisibleTo` between shipped assemblies.
- **P5 overall status when no signature verifies.** Take the most severe per-signature status, in this order: `Malformed`, `BadSignature`, `CanonicalizationMismatch`, `AlgorithmNotAllowed`, `KeyExpired`, `UntrustedKeySource`, `UnknownKey`.
- **P6 exact ETag bytes (B13).** `"` + base64url(first 16 bytes of SHA-256(payload ‖ for each kid in ordinal order: `0x0A` ‖ UTF-8(kid))) + `"`. An unsigned card hashes the payload only.
- **P7 JSON `null` on a known field** counts as absent: it is removed. Unknown fields and values inside `Struct` are kept as received, `null` included.
- **P8 header checks on verify.** `typ` is not checked (other SDKs differ). A `crit` member gives `Malformed` (RFC 7515 4.1.11). A key whose `use` is set and is not `sig` cannot be used (`AlgorithmNotAllowed`, with a message). A key whose `alg` is set and differs from the header `alg` gives `AlgorithmNotAllowed`.
- **P9 a key source that throws.** On the first load, treat it as "no usable key": log an error with the exception, and fail if `FailWhenNoKey` is set. On a later refresh, keep the last known keys and log a warning. This is the same rule as the spec's Key Vault rule.
- **P10 `jku` value.** If `JwksUrl` is relative, `jku` = `A2AServerOptions.PublicBaseUrl` + `JwksUrl`. A relative `JwksUrl` with no `PublicBaseUrl` fails startup: "CardSigningOptions.JwksUrl is relative, so A2A:PublicBaseUrl must be set to build the jku URL. Set A2A:PublicBaseUrl or use an absolute JwksUrl."
- **P11 `FailWhenNoKey = true`** is checked at startup, by loading the keys once in a hosted service. It is not checked per request.
- **P12 Key Vault algorithms.** EC P-256 signs ES256, EC P-384 signs ES384, RSA signs RS256. Other key types are rejected at first load, with a message naming the key type.
- **P13 JWKS fetch failures.** A failed fetch, a non-2xx response, a body over 64 KB or invalid JSON counts as "key not found" (`UnknownKey`). The message gives the reason, for example "The JWKS at X is larger than 64 KB." A 1-hour cache applies to successful fetches only. Failures are retried at most once per minute per URL.
- **P14 pinned key without `kid`** matches when its RFC 7638 thumbprint equals the header `kid`.
- **P15 exception type.** `JsonCanonicalizationException : FormatException` (public, `Ambinder.A2A.Protocol.Signing`).
- **P16 extended card on gRPC.** It carries the signatures computed over the JSON form of the same card model. Its JSON canonical form is the same model, so it verifies after a ProtoJSON round trip, but only when no `IAgentCardContributor` changed the JSON. Tested on JSON-RPC and REST; documented for gRPC.
- **P18 server signer seam (owner decision, 2026-11-03).** The card endpoints (`Ambinder.A2A.AspNetCore`) need the signer that lives in `Ambinder.A2A.Server`, and internals cannot cross shipped assemblies. So `Ambinder.A2A.Server` gets a public interface `IAgentCardSigner` (namespace `Ambinder.A2A.Server.Signing`) and a public property record `SignedAgentCard` (Task 8). The implementation `AgentCardSigner` and `SigningKeyCache` stay `internal`. The owner chose this over making `AgentCardSigner` itself public.
- **P17 REQUIRED beats empty-repeated.** A REQUIRED repeated or map field is kept even when empty, because the spec says REQUIRED is "always kept, even at its default". The spec example vector (Task 4) is the referee. If the vector disagrees, the vector wins: stop, report, and ask the owner before changing the rule.

## File map

Create:

```
tools/Ambinder.A2A.Tools.CardFieldTable/
  Ambinder.A2A.Tools.CardFieldTable.csproj      console, IsPackable=false
  Program.cs                                    args: <proto path> <output path>
  CardFieldTableGenerator.cs                    pure function: proto text -> C# source text

src/Ambinder.A2A.Protocol/Signing/
  JsonCanonicalizer.cs                          RFC 8785
  JsonCanonicalizationException.cs
  JcsNumberFormatter.cs                         ECMAScript Number::toString and Python repr forms (internal)
  CanonicalizationProfile.cs
  AgentCardCanonicalizer.cs
  AgentCardFieldTable.cs                        field-table types (internal)
  AgentCardFieldTable.g.cs                      GENERATED, checked in
  JsonWebKeyPublic.cs                           record + JsonWebKeySet + RFC 7638 thumbprint
  JwsAlgorithms.cs
  CardJws.cs                                    protected header, signing input, verify primitive
  A2ASigningKey.cs
  IAgentCardSigningKeySource.cs
  LocalSigningKey.cs                            ECDsa / RSA / PEM / X509Certificate2
  StaticSigningKeySource.cs

src/Ambinder.A2A.Server/Signing/
  CardSigningOptions.cs
  CardSigningServiceCollectionExtensions.cs     AddCardSigning, AddLocalCardSigningKey, AddDevelopmentCardSigningKey
  DevelopmentCardSigningKeySource.cs
  IAgentCardSigner.cs                           public seam the card endpoints call (P18)
  SignedAgentCard.cs                            public result of IAgentCardSigner.SignAsync (P18)
  SigningKeyCache.cs                            refresh, expiry filter, retired-key tracking (internal)
  AgentCardSigner.cs                            implements IAgentCardSigner: sign once per card version + key set (internal)
  CardSigningStartupCheck.cs                    IHostedService: dev-key environment, FailWhenNoKey, jku (internal)

src/Shared/
  AgentCardETag.cs                              B13 (internal; linked into Server and AspNetCore, spec 01 rule 10)

src/Ambinder.A2A.AspNetCore/Signing/
  CardJwksEndpoint.cs

src/Ambinder.A2A.Client/Signing/
  CardSignaturePolicy.cs
  CardVerificationOptions.cs
  CardVerificationStatus.cs
  CardVerificationResult.cs
  IAgentCardVerifier.cs
  DefaultAgentCardVerifier.cs
  JwksCache.cs                                  (internal)
  CardVerificationPolicyEnforcer.cs             (internal) accept / warn / reject
  A2ACardVerificationException.cs

src/Ambinder.A2A.Azure.KeyVault/
  Ambinder.A2A.Azure.KeyVault.csproj
  README.md
  AzureKeyVaultKeySourceOptions.cs
  AzureKeyVaultKeySource.cs                     shared by card and push (08)
  IKeyVaultKeyOperations.cs                     internal seam
  AzureKeyVaultKeyOperations.cs                 KeyClient + CryptographyClient adapter
  KeyVaultSigningKey.cs
  AzureKeyVaultCredentials.cs                   07's default credential rule (internal)
  AzureKeyVaultCardSigningExtensions.cs         AddAzureKeyVaultCardSigningKey

tests/Ambinder.A2A.Azure.KeyVault.Tests/        new test project
tests/vectors/card-signing/                     see Task 15 for the layout
docs/security/agent-card-trust.md               (path from package 13's tree; owner 04)
samples/CardSigning/                            the "< 10 extra lines" sample (Task 19)
```

Modify:

```
.gitattributes                                       tests/vectors/** -text
Ambinder.A2A.slnx                                    add tool, KeyVault project, KeyVault tests, sample
Directory.Packages.props                             Azure.Security.KeyVault.Keys
eng/package-dependencies.json                        KeyVault package row (01's allow-list)
NOTICE                                               attribution for copied RFC 8785 / cyberphone test data
src/Ambinder.A2A.Protocol/Json/A2AJson.cs            [JsonSerializable] and typed accessors for JsonWebKeyPublic, JsonWebKeySet
src/Ambinder.A2A.Protocol/A2ATelemetryNames.cs       ClientMeter, CardVerifications, Status
src/Ambinder.A2A.AspNetCore/.../AgentCardEndpoints.cs  (Branch B) sign + B13 ETag
src/Ambinder.A2A.Server/Tasks/TaskManager.cs         extended card is signed
src/Ambinder.A2A.AspNetCore/.../A2AEndpointRouteBuilderExtensions.cs  map card JWKS
src/Ambinder.A2A.Client/A2ACardResolver.cs           (Branch B) verify raw bytes
src/Ambinder.A2A.Client/Cards/{A2ACardResolver,A2ACardResolverOptions,ResolvedAgentCard}.cs, A2AClient.cs,
  DependencyInjection/A2AClientServiceCollectionExtensions.cs   (Branch A) package 10's files, see Task 14
docs/spec-decisions.md                               rows (Task 19)
```

Test placement: put Protocol tests in `tests/Ambinder.A2A.Protocol.Tests/Signing/` and Server tests in `tests/Ambinder.A2A.Server.Tests/Signing/`. Endpoint and end-to-end tests go in `tests/Ambinder.A2A.Integration.Tests/Signing/`. Client tests go in `tests/Ambinder.A2A.Client.Tests/Signing/` if that project exists after 01. Otherwise put them in `tests/Ambinder.A2A.Integration.Tests/Client/Signing/`.

---

### Task 0: Preflight

**Files:** none changed.

- [ ] **Step 1: Confirm package 01 landed.**

```bash
git fetch origin master && git log --oneline -1 origin/master
```

```bash
ls src/ && test -d src/Ambinder.A2A.Protocol && test -d src/Ambinder.A2A.Server && test -d src/Ambinder.A2A.AspNetCore && test -d src/Ambinder.A2A.Client && echo "01 landed"
```

```bash
git grep -nE '^namespace A2A\.' -- 'src/*.cs' | head
```

Expected: "01 landed" and no `namespace A2A.` lines. If either check fails: stop and report "package 01 has not landed".

- [ ] **Step 2: Record the dependency branches.** Run each check and write the answers into the task tracker. Later tasks read them.

```bash
git grep -n "interface IAgentCardContributor" -- src     # 03 card pipeline -> Task 9 Branch A if found
```

```bash
git grep -n "interface IA2ACardResolver" -- src         # 10 resolver -> Task 14 Branch A if found
```

```bash
ls src/*/PublicAPI.Unshipped.txt 2>/dev/null | head -3   # 02 API tracking
```

```bash
ls benchmarks/Ambinder.A2A.Benchmarks 2>/dev/null        # 14
```

```bash
ls interop 2>/dev/null; ls tests/vectors 2>/dev/null     # 16
```

```bash
git grep -n "ChainedTokenCredential" -- src              # 07 helper
```

- [ ] **Step 3: Baseline build and tests.**

```bash
dotnet build Ambinder.A2A.slnx -c Release && dotnet test --solution Ambinder.A2A.slnx -c Release --no-build
```

Expected: green. If it is red before any change, stop and report. Do not fix unrelated failures.

- [ ] **Step 4: Branch.** Work on the branch the session names. If none is named, use `feat/sdk-04-card-signing` from `origin/master`.

---

### Task 1: `JsonCanonicalizer`: structure and strings

**Files:**
- Create: `src/Ambinder.A2A.Protocol/Signing/JsonCanonicalizer.cs`, `src/Ambinder.A2A.Protocol/Signing/JsonCanonicalizationException.cs`
- Test: `tests/Ambinder.A2A.Protocol.Tests/Signing/JsonCanonicalizerTests.cs`

- [ ] **Step 1: Write the failing tests.**

```csharp
using System.Text;
using System.Text.Json;
using Ambinder.A2A.Protocol.Signing;

namespace Ambinder.A2A.Protocol.Tests.Signing;

public sealed class JsonCanonicalizerTests
{
    private static string Jcs(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Encoding.UTF8.GetString(JsonCanonicalizer.Canonicalize(doc.RootElement));
    }

    [Theory]
    [InlineData("{ \"b\" : 1 , \"a\" : [ true , false , null ] }", "{\"a\":[true,false,null],\"b\":1}")]
    [InlineData("[]", "[]")]
    [InlineData("{}", "{}")]
    [InlineData("{\"a\":{\"d\":1,\"c\":{}}}", "{\"a\":{\"c\":{},\"d\":1}}")]
    public void Removes_whitespace_and_sorts_members(string input, string expected) => Assert.Equal(expected, Jcs(input));

    [Fact]
    public void Sorts_by_utf16_code_units_not_code_points()
    {
        // U+E000 (one code unit 0xE000) sorts AFTER U+1F600 (surrogates 0xD83D 0xDE00) in UTF-16 order.
        var output = Jcs("{\"\\ue000\":1,\"\\ud83d\\ude00\":2}");
        Assert.Equal("{\"\U0001F600\":2,\"\uE000\":1}", output);
    }

    [Theory]
    [InlineData("\"\\u00e9\"", "\"é\"")]                 // non-ASCII as UTF-8, not \u
    [InlineData("\"\\u2028\"", "\"\u2028\"")]             // line separator is written literally
    [InlineData("\"\\u007f\"", "\"\u007f\"")]             // DEL is written literally
    [InlineData("\"\\/\"", "\"/\"")]                      // solidus is not escaped
    [InlineData("\"\\u0008\\t\\n\\u000c\\r\"", "\"\\b\\t\\n\\f\\r\"")]
    [InlineData("\"\\u0001\\u001f\"", "\"\\u0001\\u001f\"")] // other controls: lowercase \u00xx
    [InlineData("\"a\\\"b\\\\c\"", "\"a\\\"b\\\\c\"")]
    public void Escapes_only_what_RFC_8785_requires(string input, string expected) => Assert.Equal(expected, Jcs(input));

    [Fact]
    public void Lone_surrogate_is_an_error()
        => Assert.Throws<JsonCanonicalizationException>(() => Jcs("\"\\ud800\""));

    [Fact]
    public void Duplicate_member_names_are_an_error()
        => Assert.Throws<JsonCanonicalizationException>(() => Jcs("{\"a\":1,\"a\":2}"));

    [Fact]
    public void Buffer_writer_overload_writes_the_same_bytes()
    {
        using var doc = JsonDocument.Parse("{\"z\":\"é\",\"a\":[1,2]}");
        var writer = new System.Buffers.ArrayBufferWriter<byte>();
        JsonCanonicalizer.Canonicalize(doc.RootElement, writer);
        Assert.Equal(JsonCanonicalizer.Canonicalize(doc.RootElement), writer.WrittenSpan.ToArray());
    }
}
```

- [ ] **Step 2: Run and see it fail.**

```bash
dotnet test --project tests/Ambinder.A2A.Protocol.Tests -- --filter-class "*JsonCanonicalizerTests"
```

Expected: build error, because `JsonCanonicalizer` does not exist yet. (If the runner's filter syntax differs, use the one the repo's CI uses.)

- [ ] **Step 3: Implement.**

`JsonCanonicalizationException.cs`:

```csharp
namespace Ambinder.A2A.Protocol.Signing;

/// <summary>The JSON value cannot be written in RFC 8785 (JCS) form: for example a lone surrogate, a duplicate member name, or a number JCS cannot keep exactly.</summary>
public sealed class JsonCanonicalizationException(string message) : FormatException(message);
```

`JsonCanonicalizer.cs`. Numbers go through `JcsNumberFormatter` (Task 2). For this task, write numbers with a temporary `WriteNumber` that calls `JcsNumberFormatter.Format`, and stub `Format` with `throw new NotImplementedException()`. Task 1's tests use only the integers 1 and 2. To keep them green, the stub may special-case raw integer text that has no `.`, `e` or `E` and whose absolute value is at most 2^53−1; for those it writes the raw text without leading `+` or leading zeros. Task 2 replaces the stub.

```csharp
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Ambinder.A2A.Protocol.Signing;

/// <summary>RFC 8785 JSON Canonicalization Scheme (JCS).</summary>
public static class JsonCanonicalizer
{
    /// <summary>Returns the JCS form of <paramref name="value"/> as UTF-8 bytes.</summary>
    /// <exception cref="JsonCanonicalizationException">The value has no JCS form.</exception>
    public static byte[] Canonicalize(JsonElement value)
    {
        var writer = new ArrayBufferWriter<byte>(1024);
        Canonicalize(value, writer);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>Writes the JCS form of <paramref name="value"/> to <paramref name="output"/>.</summary>
    /// <exception cref="JsonCanonicalizationException">The value has no JCS form.</exception>
    public static void Canonicalize(JsonElement value, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        WriteValue(value, output, JcsFormat.Rfc8785);
    }

    internal static void WriteValue(JsonElement value, IBufferWriter<byte> o, JcsFormat format)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object: WriteObject(value, o, format); break;
            case JsonValueKind.Array:
                Ascii(o, "[");
                var first = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!first) Ascii(o, ",");
                    first = false;
                    WriteValue(item, o, format);
                }
                Ascii(o, "]");
                break;
            case JsonValueKind.String: WriteString(GetString(value), o, format); break;
            case JsonValueKind.Number: Ascii(o, JcsNumberFormatter.Format(value, format)); break;
            case JsonValueKind.True: Ascii(o, "true"); break;
            case JsonValueKind.False: Ascii(o, "false"); break;
            case JsonValueKind.Null: Ascii(o, "null"); break;
            default: throw new JsonCanonicalizationException($"Unexpected JSON value kind {value.ValueKind}.");
        }
    }

    private static void WriteObject(JsonElement value, IBufferWriter<byte> o, JcsFormat format)
    {
        var members = new List<(string Name, JsonElement Value)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in value.EnumerateObject())
        {
            if (!seen.Add(p.Name)) throw new JsonCanonicalizationException($"Duplicate member name '{p.Name}'. JCS needs I-JSON (RFC 7493), which forbids duplicates.");
            members.Add((p.Name, p.Value));
        }
        members.Sort((a, b) => CompareNames(a.Name, b.Name, format));
        Ascii(o, "{");
        for (var i = 0; i < members.Count; i++)
        {
            if (i > 0) Ascii(o, ",");
            WriteString(members[i].Name, o, format);
            Ascii(o, ":");
            WriteValue(members[i].Value, o, format);
        }
        Ascii(o, "}");
    }

    /// <summary>RFC 8785 3.2.3: UTF-16 code units. The Python 1.1.2 variant sorts by code point.</summary>
    internal static int CompareNames(string a, string b, JcsFormat format)
    {
        if (format != JcsFormat.Python112) return string.CompareOrdinal(a, b);
        var ea = a.EnumerateRunes();
        var eb = b.EnumerateRunes();
        while (true)
        {
            var ha = ea.MoveNext();
            var hb = eb.MoveNext();
            if (!ha || !hb) return ha == hb ? 0 : ha ? 1 : -1;
            var c = ea.Current.Value.CompareTo(eb.Current.Value);
            if (c != 0) return c;
        }
    }

    internal static string GetString(JsonElement value)
    {
        try { return value.GetString()!; }
        catch (InvalidOperationException ex) { throw new JsonCanonicalizationException($"A string is not valid UTF-16: {ex.Message}"); }
    }

    internal static void WriteString(string s, IBufferWriter<byte> o, JcsFormat format)
    {
        Ascii(o, "\"");
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            string? escape = c switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                < ' ' => $"\\u{(int)c:x4}",
                _ => null,
            };
            if (escape is null && char.IsSurrogate(c))
            {
                if (!char.IsHighSurrogate(c) || i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))
                    throw new JsonCanonicalizationException("A string has a lone surrogate. JCS needs valid Unicode.");
                if (format == JcsFormat.Python112) escape = $"\\u{(int)c:x4}\\u{(int)s[i + 1]:x4}";
                else { i++; continue; }
                Utf8(o, s.AsSpan(start, i - start)); Ascii(o, escape); i++; start = i + 1; continue;
            }
            // Python json.dumps(ensure_ascii=True) escapes everything outside 0x20..0x7E.
            if (escape is null && format == JcsFormat.Python112 && c > '~') escape = $"\\u{(int)c:x4}";
            if (escape is null) continue;
            Utf8(o, s.AsSpan(start, i - start));
            Ascii(o, escape);
            start = i + 1;
        }
        Utf8(o, s.AsSpan(start));
        Ascii(o, "\"");
    }

    internal static void Ascii(IBufferWriter<byte> o, string s)
    {
        var span = o.GetSpan(s.Length);
        for (var i = 0; i < s.Length; i++) span[i] = (byte)s[i];
        o.Advance(s.Length);
    }

    private static void Utf8(IBufferWriter<byte> o, ReadOnlySpan<char> s)
    {
        if (s.IsEmpty) return;
        var span = o.GetSpan(Encoding.UTF8.GetMaxByteCount(s.Length));
        o.Advance(Encoding.UTF8.GetBytes(s, span));
    }
}

/// <summary>Output dialect. Only <see cref="Rfc8785"/> is JCS; the other exists to diagnose cards signed by Python SDK 1.1.2.</summary>
internal enum JcsFormat { Rfc8785, Python112 }
```

- [ ] **Step 4: Run and see it pass.** Same command as Step 2. Expected: all pass. If `JsonElement.GetString()` does not throw for `"\ud800"` but returns a lone surrogate, the `char.IsSurrogate` branch throws instead. Either way the test sees `JsonCanonicalizationException`.

- [ ] **Step 5: Commit.** `feat(signing): RFC 8785 canonicalizer (structure and strings)`

---

### Task 2: JCS numbers and the RFC 8785 test vectors

**Files:**
- Create: `src/Ambinder.A2A.Protocol/Signing/JcsNumberFormatter.cs`
- Create: `tests/vectors/card-signing/jcs/*` (copied test data), `tests/vectors/card-signing/README.md`
- Modify: `.gitattributes`, `NOTICE`, `tests/Ambinder.A2A.Protocol.Tests/Ambinder.A2A.Protocol.Tests.csproj` (copy `tests/vectors/**` to the output folder)
- Test: `tests/Ambinder.A2A.Protocol.Tests/Signing/JcsNumberTests.cs`, `tests/Ambinder.A2A.Protocol.Tests/Signing/JcsVectorTests.cs`

- [ ] **Step 1: Get the vectors.** Copy them verbatim. Do not retype them.
  - The RFC 8785 number examples: RFC 8785 Appendix B, "Number Serialization Samples". Put them in `jcs/numbers.txt` as `<IEEE-754 hex> <expected>` lines, exactly as in the RFC.
  - The input/output pairs from the JCS reference implementation's `testdata/input` and `testdata/output` (repository `cyberphone/json-canonicalization`, Apache-2.0). Store them as `jcs/<name>.input.json` and `jcs/<name>.output.json`.
  - Add one line to `NOTICE` naming the source repository, its license, and the commit you copied from.
  - Add one line per vector file to `tests/vectors/card-signing/README.md`: file, source, upstream issue (none for these).
  - Add `tests/vectors/** -text` to `.gitattributes` **before** adding the files.
  - If either source cannot be fetched from the session, stop and report. Do not write vectors from memory.

- [ ] **Step 2: Write the failing tests.**

```csharp
public sealed class JcsNumberTests
{
    private static string Num(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Encoding.UTF8.GetString(JsonCanonicalizer.Canonicalize(doc.RootElement));
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("-0", "0")]
    [InlineData("1", "1")]
    [InlineData("-1.5", "-1.5")]
    [InlineData("1e-6", "0.000001")]
    [InlineData("1e-7", "1e-7")]
    [InlineData("1e20", "100000000000000000000")]
    [InlineData("1e21", "1e+21")]
    [InlineData("123.456", "123.456")]
    [InlineData("1.0", "1")]
    [InlineData("100", "100")]
    [InlineData("9007199254740991", "9007199254740991")]
    [InlineData("4.5e15", "4500000000000000")]
    public void Formats_like_ECMAScript(string input, string expected) => Assert.Equal(expected, Num(input));

    [Theory]
    [InlineData("9007199254740992")]
    [InlineData("-9007199254740992")]
    [InlineData("12345678901234567890")]
    public void Integers_outside_the_safe_range_are_an_error(string input)
    {
        var ex = Assert.Throws<JsonCanonicalizationException>(() => Num(input));
        Assert.Contains("cannot be represented exactly in JCS", ex.Message);
    }

    [Fact]
    public void Overflow_is_an_error() => Assert.Throws<JsonCanonicalizationException>(() => Num("1e400"));

    public static TheoryData<string, string> RfcSamples() { /* read jcs/numbers.txt from the output folder */ }

    [Theory, MemberData(nameof(RfcSamples))]
    public void RFC_8785_appendix_B(string ieeeHex, string expected)
    {
        var d = BitConverter.Int64BitsToDouble(Convert.ToInt64(ieeeHex, 16));
        if (!double.IsFinite(d)) { Assert.Throws<JsonCanonicalizationException>(() => JcsNumberFormatter.FormatDouble(d, JcsFormat.Rfc8785)); return; }
        Assert.Equal(expected, JcsNumberFormatter.FormatDouble(d, JcsFormat.Rfc8785));
    }
}
```

`JcsVectorTests`: for each `jcs/*.input.json`, canonicalize it and assert the bytes equal `jcs/<name>.output.json` exactly. `JcsNumberFormatter` is internal. If Protocol.Tests has no `InternalsVisibleTo` (it is allowed for the matching test assembly, 01 rule 6), add it in `Ambinder.A2A.Protocol.csproj`.

Appendix B lists NaN and Infinity as non-serializable. The `RfcSamples` reader must map those rows to the "throws" branch.

- [ ] **Step 3: Run and see it fail.**

- [ ] **Step 4: Implement `JcsNumberFormatter`.**

```csharp
using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Ambinder.A2A.Protocol.Signing;

internal static class JcsNumberFormatter
{
    private const long MaxSafeInteger = 9007199254740991; // 2^53 - 1

    public static string Format(JsonElement number, JcsFormat format)
    {
        var raw = number.GetRawText();
        if (raw.AsSpan().IndexOfAny('.', 'e', 'E') < 0)
        {
            var big = BigInteger.Parse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (BigInteger.Abs(big) > MaxSafeInteger)
                throw new JsonCanonicalizationException($"The number {raw} cannot be represented exactly in JCS (it is outside ±(2^53-1)). Send it as a string.");
            return big.IsZero ? "0" : big.ToString(CultureInfo.InvariantCulture);
        }
        if (!number.TryGetDouble(out var d)) throw new JsonCanonicalizationException($"The number {raw} is not a finite IEEE 754 double.");
        return FormatDouble(d, format);
    }

    /// <summary>ECMAScript Number::toString (ECMA-262 Number.prototype.toString, radix 10), which RFC 8785 3.2.2.3 uses.</summary>
    public static string FormatDouble(double value, JcsFormat format)
    {
        if (!double.IsFinite(value)) throw new JsonCanonicalizationException("NaN and Infinity have no JSON form.");
        if (value == 0) return "0";                       // also -0
        if (value < 0) return "-" + FormatDouble(-value, format);

        var (digits, n) = ShortestDigits(value);          // value = 0.digits × 10^n, digits has no leading/trailing zeros
        var k = digits.Length;
        if (format == JcsFormat.Python112) return PythonRepr(digits, n);

        if (k <= n && n <= 21) return digits + new string('0', n - k);
        if (0 < n && n <= 21) return digits[..n] + "." + digits[n..];
        if (-6 < n && n <= 0) return "0." + new string('0', -n) + digits;
        var e = n - 1;
        var exp = (e < 0 ? "-" : "+") + Math.Abs(e).ToString(CultureInfo.InvariantCulture);
        return k == 1 ? digits + "e" + exp : digits[0] + "." + digits[1..] + "e" + exp;
    }

    /// <summary>Shortest round-trip digits. .NET Core 3.0+ "R" is the shortest string that round-trips (IEEE-correct).</summary>
    private static (string Digits, int N) ShortestDigits(double value)
    {
        var r = value.ToString("R", CultureInfo.InvariantCulture);
        var exp = 0;
        var ePos = r.IndexOfAny(['E', 'e']);
        if (ePos >= 0)
        {
            exp = int.Parse(r.AsSpan(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            r = r[..ePos];
        }
        var dot = r.IndexOf('.');
        var digits = dot >= 0 ? r[..dot] + r[(dot + 1)..] : r;
        var n = (dot >= 0 ? dot : r.Length) + exp;
        var lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0') lead++;
        digits = digits[lead..].TrimEnd('0');
        n -= lead;
        return (digits, n);
    }

    /// <summary>Python repr(float): exponent form when the decimal exponent is &lt; -4 or &gt;= 16; exponent has at least 2 digits.</summary>
    private static string PythonRepr(string digits, int n)
    {
        var e = n - 1;
        if (e < -4 || e >= 16)
        {
            var mant = digits.Length == 1 ? digits : digits[0] + "." + digits[1..];
            return mant + "e" + (e < 0 ? "-" : "+") + Math.Abs(e).ToString("00", CultureInfo.InvariantCulture);
        }
        if (n <= 0) return "0." + new string('0', -n) + digits;
        if (n >= digits.Length) return digits + new string('0', n - digits.Length) + ".0";
        return digits[..n] + "." + digits[n..];
    }
}
```

Remove the Task 1 stub. Python repr only applies to JSON numbers with a fraction or exponent in the raw text. Raw integers stay integers in every format, because Python reads them as `int`.

- [ ] **Step 5: Run and see all JCS tests pass**, including every RFC sample and every cyberphone pair. If a sample fails, fix the formatter. Never change a vector. Optional, not in CI: if the session can fetch the reference implementation's 100-million-number file, run it once locally and record the result in the PR body.

- [ ] **Step 6: Commit.** `feat(signing): JCS number formatting and RFC 8785 vectors`

---

### Task 3: Card field table, generated from the proto

**Files:**
- Create: `tools/Ambinder.A2A.Tools.CardFieldTable/{csproj,Program.cs,CardFieldTableGenerator.cs}`
- Create: `src/Ambinder.A2A.Protocol/Signing/AgentCardFieldTable.cs`, `src/Ambinder.A2A.Protocol/Signing/AgentCardFieldTable.g.cs` (generated)
- Modify: `Ambinder.A2A.slnx` (add the tool under a `/tools/` folder)
- Test: `tests/Ambinder.A2A.Protocol.Tests/Signing/AgentCardFieldTableTests.cs`

- [ ] **Step 1: Define the table types** (`AgentCardFieldTable.cs`, internal):

```csharp
using System.Collections.Frozen;

namespace Ambinder.A2A.Protocol.Signing;

internal enum FieldPresence { Required, Optional, Plain }       // REQUIRED field_behavior; proto3 `optional`; neither
internal enum FieldShape { Scalar, Message, Repeated, Map, Struct }  // Struct = google.protobuf.Struct/Value/ListValue

internal sealed record CardField(string JsonName, FieldPresence Presence, FieldShape Shape, string? ElementMessage, string? ZeroEnumName);
internal sealed record CardMessage(string Name, FrozenDictionary<string, CardField> Fields);

internal static partial class AgentCardFieldTable
{
    public static CardMessage AgentCard => Messages["AgentCard"];
}
```

The rules for the fields:
- `ElementMessage` is the message type of a `Message` field, of a repeated message element, or of a map's message value. It is `null` for scalar elements.
- `ZeroEnumName` is set for enum scalars (the proto name of value 0). No card field is an enum in v1.0.1; the field is there for safety.
- Oneof members have `Shape = Message` and `Presence = Plain`. Messages have presence, so present means kept.

- [ ] **Step 2: Write the failing tests.**

```csharp
public sealed class AgentCardFieldTableTests
{
    private static string RepoFile(params string[] parts) { /* walk up from AppContext.BaseDirectory to the folder holding Ambinder.A2A.slnx */ }

    [Fact]
    public void Generated_table_is_up_to_date()
    {
        var proto = File.ReadAllText(RepoFile("proto", "a2a.proto"));
        var expected = CardFieldTableGenerator.Generate(proto);
        var actual = File.ReadAllText(RepoFile("src", "Ambinder.A2A.Protocol", "Signing", "AgentCardFieldTable.g.cs"));
        Assert.True(expected == actual,
            "AgentCardFieldTable.g.cs is stale. Run: dotnet run --project tools/Ambinder.A2A.Tools.CardFieldTable -- proto/a2a.proto src/Ambinder.A2A.Protocol/Signing/AgentCardFieldTable.g.cs");
    }

    [Theory]
    [InlineData("AgentCard", "name", "Required", "Scalar")]
    [InlineData("AgentCard", "documentationUrl", "Optional", "Scalar")]
    [InlineData("AgentCard", "provider", "Plain", "Message")]
    [InlineData("AgentCard", "capabilities", "Required", "Message")]
    [InlineData("AgentCard", "securitySchemes", "Plain", "Map")]
    [InlineData("AgentCard", "skills", "Required", "Repeated")]
    [InlineData("AgentCapabilities", "streaming", "Optional", "Scalar")]
    [InlineData("AgentExtension", "params", "Plain", "Struct")]
    [InlineData("AgentExtension", "required", "Plain", "Scalar")]
    [InlineData("SecurityScheme", "oauth2SecurityScheme", "Plain", "Message")]
    [InlineData("OAuth2SecurityScheme", "oauth2MetadataUrl", "Plain", "Scalar")]
    [InlineData("AuthorizationCodeOAuthFlow", "scopes", "Required", "Map")]
    [InlineData("SecurityRequirement", "schemes", "Plain", "Map")]
    [InlineData("StringList", "list", "Plain", "Repeated")]
    public void Spot_checks(string message, string jsonName, string presence, string shape)
    {
        var f = AgentCardFieldTable.Messages[message].Fields[jsonName];
        Assert.Equal(presence, f.Presence.ToString());
        Assert.Equal(shape, f.Shape.ToString());
    }

    [Fact]
    public void Every_member_of_a_full_card_has_a_table_entry()
    {
        // A card that sets every field of every card message, incl. all five security schemes and all five OAuth flows,
        // serialized by the SDK serializer. Walk it with the table; any member not found is a failure (names listed).
        var json = JsonSerializer.SerializeToElement(FullCards.EveryField(), A2AJsonContext.Default.AgentCard);
        var missing = FieldTableWalker.UnknownMembers(json);   // test helper: walks the JSON with the table
        Assert.True(missing.Count == 0, "Card members without a field-table entry: " + string.Join(", ", missing));
    }
}
```

`FullCards.EveryField()` is a test helper. It builds an `AgentCard` with every property set to a non-default value: every `SecurityScheme` variant, every `OAuthFlows` variant (each in its own scheme, because `OAuthFlows` is a oneof), and skills with every list set. Put it in `tests/Ambinder.A2A.Protocol.Tests/Signing/FullCards.cs`. Tasks 4 and 15 reuse it.

The tool's `CardFieldTableGenerator.cs` is linked into the test project with `<Compile Include="../../tools/Ambinder.A2A.Tools.CardFieldTable/CardFieldTableGenerator.cs" Link="Signing/CardFieldTableGenerator.cs" />`. So the test needs no reference to an exe project.

- [ ] **Step 3: Run and see it fail.**

- [ ] **Step 4: Implement the generator.**
  - `CardFieldTableGenerator.Generate(string protoText) -> string`. It is pure and deterministic: ordinal sort of messages and fields, `\n` line endings, and a header comment `// <auto-generated> from proto/a2a.proto by tools/Ambinder.A2A.Tools.CardFieldTable. Do not edit. </auto-generated>`.
  - Parse with a small hand-written tokenizer. Strip `//` comments and string literals, then track `message Name {`, `oneof name {`, `}` and field statements of the form `[optional|repeated] <type|map<K,V>> <name> = <n> [options];`. Nested `oneof` blocks belong to the enclosing message. REQUIRED is the text `(google.api.field_behavior) = REQUIRED` inside the options brackets.
  - Start at `AgentCard`. Breadth-first, include every message type a field references that is defined in the same file. `google.protobuf.Struct`, `Value` and `ListValue` become `FieldShape.Struct` and are not walked. `AgentCardSignature` is included too (harmless; the canonicalizer drops top-level `signatures` before it looks anything up).
  - JSON name: the proto3 lowerCamel rule. Remove each `_` and upper-case the letter after it; digits stay as they are (`oauth2_metadata_url` → `oauth2MetadataUrl`, `mtls_security_scheme` → `mtlsSecurityScheme`). If a field has a `json_name` option, use it.
  - Emit `internal static partial class AgentCardFieldTable { public static readonly FrozenDictionary<string, CardMessage> Messages = ... }` with literal entries.
  - `Program.cs`: `File.WriteAllText(args[1], CardFieldTableGenerator.Generate(File.ReadAllText(args[0])))`.
  - Generate the checked-in file:

    ```bash
    dotnet run --project tools/Ambinder.A2A.Tools.CardFieldTable -- proto/a2a.proto src/Ambinder.A2A.Protocol/Signing/AgentCardFieldTable.g.cs
    ```

- [ ] **Step 5: Run the tests and see them pass.** If `Every_member_of_a_full_card_has_a_table_entry` lists a member, the model has a field the proto does not (or the names differ). Report it; do not hand-edit the table.

- [ ] **Step 6: Commit.** `feat(signing): card field table generated from a2a.proto`

---

### Task 4: `AgentCardCanonicalizer`, presence vectors and the spec example

**Files:**
- Create: `src/Ambinder.A2A.Protocol/Signing/{CanonicalizationProfile.cs,AgentCardCanonicalizer.cs}`
- Create: `tests/vectors/card-signing/presence/*`, `tests/vectors/card-signing/spec-example/*`
- Test: `tests/Ambinder.A2A.Protocol.Tests/Signing/AgentCardCanonicalizerTests.cs`, `.../PresenceVectorTests.cs`, `.../SpecExampleVectorTests.cs`

- [ ] **Step 1: Get the spec example.** Find A2A PR #2319 (`https://github.com/a2aproject/A2A/pull/2319`, "JCS canonicalization example made a valid card") and its merge commit. In the specification document at that commit, find the section 8.4 example. Copy **verbatim**:
  - the example card → `spec-example/card.json`
  - the canonical form the spec shows → `spec-example/expected-payload.json` (no trailing newline; use the exact bytes the spec shows)

  Write `spec-example/SOURCE.md` with the PR URL, the commit SHA, the file path and the section. If the spec shows no canonical output, or the session cannot reach GitHub, stop and report. Do not derive the expected bytes with our own code; that would test the code against itself.

- [ ] **Step 2: Write the presence vectors.** These are hand-written. Each is a pair `presence/<case>.card.json` → `presence/<case>.payload.json`, plus one README line each. The expected payloads follow spec 04's rules, written out by hand:

| Case | Input highlights | Expected in payload |
|---|---|---|
| `required-at-default` | `"version": ""`, `"description": ""` | both kept as `""` |
| `optional-set-to-default` | `"capabilities": {"streaming": false}` | `"streaming":false` kept |
| `optional-absent` | no `documentationUrl` | absent |
| `plain-scalar-default` | extension `"required": false`, `"description": ""` | both removed |
| `empty-repeated-plain` | skill `"examples": []` | removed |
| `empty-repeated-required` | skill `"tags": []` | kept as `[]` (P17) |
| `empty-message` | `"provider": {}` | kept as `{}` |
| `null-known-field` | `"iconUrl": null` | removed (P7) |
| `unknown-field` | top-level `"futureField": {"a": 0, "b": ""}` | kept exactly, defaults inside too |
| `struct-with-defaults` | extension `"params": {"x": 0, "y": "", "z": []}` | kept exactly |
| `signatures-dropped` | `"signatures": [...]` | absent |
| `nested-signatures-kept` | `"futureField": {"signatures": 1}` | kept (only the top-level member is dropped) |
| `map-of-messages` | `securitySchemes` with two keys, unsorted | keys sorted; each value canonicalized as `SecurityScheme` |

- [ ] **Step 3: Write the failing tests.**

```csharp
public sealed class AgentCardCanonicalizerTests
{
    [Fact]
    public void Drops_top_level_signatures_and_canonicalizes()
    {
        using var doc = JsonDocument.Parse("""{"name":"a","signatures":[{"protected":"x","signature":"y"}],"description":"d"}""");
        Assert.Equal("""{"description":"d","name":"a"}""", Encoding.UTF8.GetString(AgentCardCanonicalizer.GetSigningPayload(doc.RootElement)));
    }

    [Fact]
    public void Non_object_is_an_error()
    {
        using var doc = JsonDocument.Parse("[]");
        Assert.Throws<JsonCanonicalizationException>(() => AgentCardCanonicalizer.GetSigningPayload(doc.RootElement));
    }

    [Fact]
    public void Serializer_round_trip_gives_the_same_payload()
    {
        // Our serializer output, re-ordered and re-indented, has the same payload.
        var json = JsonSerializer.SerializeToUtf8Bytes(FullCards.EveryField(), A2AJsonContext.Default.AgentCard);
        using var a = JsonDocument.Parse(json);
        using var b = JsonDocument.Parse(Reindent(json));   // helper: pretty-print with members in reverse order
        Assert.Equal(AgentCardCanonicalizer.GetSigningPayload(a.RootElement), AgentCardCanonicalizer.GetSigningPayload(b.RootElement));
    }

    [Fact]
    public void DropRequiredDefaults_profile_removes_required_defaults()
    {
        using var doc = JsonDocument.Parse("""{"name":"a","version":"","skills":[]}""");
        Assert.Equal("""{"name":"a"}""",
            Encoding.UTF8.GetString(AgentCardCanonicalizer.GetSigningPayload(doc.RootElement, CanonicalizationProfile.DropRequiredDefaults)));
    }

    [Fact]
    public void Python112_profile_escapes_non_ascii_and_uses_python_floats()
    {
        using var doc = JsonDocument.Parse("""{"name":"é","capabilities":{"extensions":[{"uri":"u","params":{"v":1e-6}}]}}""");
        Assert.Equal("""{"capabilities":{"extensions":[{"params":{"v":1e-06},"uri":"u"}]},"name":"\u00e9"}""",
            Encoding.UTF8.GetString(AgentCardCanonicalizer.GetSigningPayload(doc.RootElement, CanonicalizationProfile.Python112)));
    }
}
```

`PresenceVectorTests`: one theory row per `presence/*.card.json`. Assert the payload bytes equal the `.payload.json` bytes. `SpecExampleVectorTests`: one fact, the same comparison.

- [ ] **Step 4: Run and see them fail.**

- [ ] **Step 5: Implement.**

```csharp
namespace Ambinder.A2A.Protocol.Signing;

/// <summary>Which canonical form to build. Only <see cref="Spec"/> is used for signing.</summary>
/// <remarks>The other values reproduce known non-spec forms of other SDKs, so a verifier can tell format drift from tampering (A2A #2122).</remarks>
public enum CanonicalizationProfile
{
    /// <summary>Spec 8.4 as this SDK reads it: REQUIRED kept, optional kept when present, defaults removed, RFC 8785.</summary>
    Spec = 0,
    /// <summary>Like <see cref="Spec"/>, but REQUIRED fields at their default value are removed too (Python and JS SDKs today).</summary>
    DropRequiredDefaults = 1,
    /// <summary><see cref="DropRequiredDefaults"/> plus Python 1.1.2 output: non-ASCII escaped, code-point key order, Python float repr.</summary>
    Python112 = 2,
}
```

`AgentCardCanonicalizer`:

```csharp
public static class AgentCardCanonicalizer
{
    /// <summary>The bytes that are signed: card JSON minus signatures and default-valued fields, in JCS form.</summary>
    public static byte[] GetSigningPayload(JsonElement cardJson) => GetSigningPayload(cardJson, CanonicalizationProfile.Spec);

    /// <summary>The signing payload in the given canonical form. Use anything other than <see cref="CanonicalizationProfile.Spec"/> for diagnosis only.</summary>
    public static byte[] GetSigningPayload(JsonElement cardJson, CanonicalizationProfile profile)
    {
        if (cardJson.ValueKind != JsonValueKind.Object) throw new JsonCanonicalizationException("An agent card must be a JSON object.");
        var o = new ArrayBufferWriter<byte>(4096);
        var format = profile == CanonicalizationProfile.Python112 ? JcsFormat.Python112 : JcsFormat.Rfc8785;
        WriteMessage(cardJson, AgentCardFieldTable.AgentCard, profile, format, o, topLevel: true);
        return o.WrittenSpan.ToArray();
    }

    private static void WriteMessage(JsonElement obj, CardMessage message, CanonicalizationProfile profile, JcsFormat format, IBufferWriter<byte> o, bool topLevel)
    {
        var kept = new List<(string Name, JsonElement Value, CardField? Field)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject())
        {
            if (!seen.Add(p.Name)) throw new JsonCanonicalizationException($"Duplicate member name '{p.Name}'.");
            if (topLevel && p.Name == "signatures") continue;
            if (!message.Fields.TryGetValue(p.Name, out var field)) { kept.Add((p.Name, p.Value, null)); continue; } // unknown: kept as received
            if (Keep(p.Value, field, profile)) kept.Add((p.Name, p.Value, field));
        }
        kept.Sort((a, b) => JsonCanonicalizer.CompareNames(a.Name, b.Name, format));
        JsonCanonicalizer.Ascii(o, "{");
        for (var i = 0; i < kept.Count; i++)
        {
            if (i > 0) JsonCanonicalizer.Ascii(o, ",");
            JsonCanonicalizer.WriteString(kept[i].Name, o, format);
            JsonCanonicalizer.Ascii(o, ":");
            WriteField(kept[i].Value, kept[i].Field, profile, format, o);
        }
        JsonCanonicalizer.Ascii(o, "}");
    }

    private static void WriteField(JsonElement value, CardField? field, CanonicalizationProfile profile, JcsFormat format, IBufferWriter<byte> o)
    {
        if (field is null || field.ElementMessage is null || field.Shape is FieldShape.Struct or FieldShape.Scalar)
        {
            JsonCanonicalizer.WriteValue(value, o, format);
            return;
        }
        var element = AgentCardFieldTable.Messages[field.ElementMessage];
        switch (field.Shape)
        {
            case FieldShape.Message when value.ValueKind == JsonValueKind.Object:
                WriteMessage(value, element, profile, format, o, topLevel: false); return;
            case FieldShape.Repeated when value.ValueKind == JsonValueKind.Array:
                JsonCanonicalizer.Ascii(o, "[");
                var first = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!first) JsonCanonicalizer.Ascii(o, ",");
                    first = false;
                    if (item.ValueKind == JsonValueKind.Object) WriteMessage(item, element, profile, format, o, false);
                    else JsonCanonicalizer.WriteValue(item, o, format);
                }
                JsonCanonicalizer.Ascii(o, "]"); return;
            case FieldShape.Map when value.ValueKind == JsonValueKind.Object:
                var entries = value.EnumerateObject().Select(p => (p.Name, p.Value)).ToList();
                entries.Sort((a, b) => JsonCanonicalizer.CompareNames(a.Name, b.Name, format));
                JsonCanonicalizer.Ascii(o, "{");
                for (var i = 0; i < entries.Count; i++)
                {
                    if (i > 0) JsonCanonicalizer.Ascii(o, ",");
                    JsonCanonicalizer.WriteString(entries[i].Name, o, format);
                    JsonCanonicalizer.Ascii(o, ":");
                    if (entries[i].Value.ValueKind == JsonValueKind.Object) WriteMessage(entries[i].Value, element, profile, format, o, false);
                    else JsonCanonicalizer.WriteValue(entries[i].Value, o, format);
                }
                JsonCanonicalizer.Ascii(o, "}"); return;
            default:
                JsonCanonicalizer.WriteValue(value, o, format); return;   // wrong JSON kind for the field: keep as received
        }
    }

    private static bool Keep(JsonElement v, CardField f, CanonicalizationProfile profile)
    {
        if (v.ValueKind == JsonValueKind.Null) return false;                               // P7
        if (f.Presence == FieldPresence.Required && profile == CanonicalizationProfile.Spec) return true;
        if (f.Presence == FieldPresence.Optional) return true;
        return f.Shape switch
        {
            FieldShape.Scalar => !IsDefaultScalar(v, f),
            FieldShape.Repeated => !(v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 0),
            FieldShape.Map => !(v.ValueKind == JsonValueKind.Object && !v.EnumerateObject().MoveNext()),
            FieldShape.Message => true,                                                      // messages have presence
            FieldShape.Struct => true,
            _ => true,
        };
    }

    private static bool IsDefaultScalar(JsonElement v, CardField f) => v.ValueKind switch
    {
        JsonValueKind.String => v.ValueEquals("") || (f.ZeroEnumName is { } zero && v.ValueEquals(zero)),
        JsonValueKind.False => true,
        JsonValueKind.Number => v.TryGetDouble(out var d) && d == 0,
        _ => false,
    };
}
```

In the `DropRequiredDefaults` and `Python112` profiles, a REQUIRED field is judged like a plain field. A REQUIRED **message** is still kept when present, because messages have presence. Whether Python drops an empty `capabilities: {}` is not known; Task 13 confirms the variants against peer vectors when they exist.

- [ ] **Step 6: Run and see all pass**, including the spec example. If the spec example fails because of P17 or any other field rule, stop and report the diff. Do not change the vector, and do not change a rule without the owner.

- [ ] **Step 7: Commit.** `feat(signing): agent card canonicalization (spec 8.4 field rules)`

---

### Task 5: Keys and JWS primitives

**Files:**
- Create: `src/Ambinder.A2A.Protocol/Signing/{JsonWebKeyPublic.cs,JwsAlgorithms.cs,CardJws.cs,A2ASigningKey.cs,IAgentCardSigningKeySource.cs}`
- Modify: `src/Ambinder.A2A.Protocol/Json/A2AJson.cs` (add `[JsonSerializable(typeof(JsonWebKeyPublic))]`, `[JsonSerializable(typeof(JsonWebKeySet))]`, and the public typed accessors `A2AJson.JsonWebKeyPublic` and `A2AJson.JsonWebKeySet`, built from `A2AJson.Options` the way the 02 plan, Task 2.2, builds the others)
- Test: `tests/Ambinder.A2A.Protocol.Tests/Signing/{JsonWebKeyPublicTests.cs,CardJwsTests.cs}`

- [ ] **Step 1: Write the failing tests.**
  - `JsonWebKeyPublic.FromECDsa(ecdsa)` gives `kty=EC`, `crv=P-256`, base64url `x` and `y` of 32 bytes each. `FromRsa(rsa)` gives `kty=RSA` with `n` and `e`. Neither has a `d`, and serializing with `A2AJsonContext` never writes `d` or `notAfter`.
  - RFC 7638 thumbprint: the RSA key in RFC 7638 section 3.1 has the thumbprint `NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs`. Copy the key's `n` and `e` from RFC 7638 3.1. Do not retype them; fetch the RFC.
  - `CardJws.CreateProtectedHeader("ES256", "k1", null)` decodes to the JSON `{"alg":"ES256","kid":"k1","typ":"JOSE"}` (JCS order). With a `jku`, the header has `jku` too.
  - `CardJws.CreateSigningInput(protectedB64, payload)` is ASCII of `protectedB64 + "." + base64url(payload)`.
  - Round trip for each algorithm (ES256, ES384, PS256, RS256): sign the input with a local key (Task 6 is not done yet, so use BCL `ECDsa`/`RSA` directly), and `CardJws.Verify(alg, jwk, input, sig)` returns `JwsVerifyOutcome.Valid`. One flipped byte in the payload gives `Invalid`.
  - Key/alg mismatch gives `KeyNotUsable`, with a reason: an ES256 header with a P-384 key, an RS256 header with an EC key, RSA under 2048 bits, a JWK `use` of `enc`, a JWK `alg` different from the header (P8).
  - An ES256 signature that is not 64 bytes gives `Invalid`. It does not throw.

- [ ] **Step 2: Run and see them fail.**

- [ ] **Step 3: Implement.**

```csharp
/// <summary>The public members of a JSON Web Key (RFC 7517). Never holds private key material.</summary>
public sealed record JsonWebKeyPublic
{
    public required string Kty { get; init; }
    public string? Crv { get; init; }
    public string? X { get; init; }
    public string? Y { get; init; }
    public string? N { get; init; }
    public string? E { get; init; }
    public string? Kid { get; init; }
    public string? Alg { get; init; }
    public string? Use { get; init; }

    /// <summary>For keys pinned in <c>CardVerificationOptions.TrustedKeys</c>: after this time a signature by this key is <c>KeyExpired</c>. Not part of a JWKS.</summary>
    [JsonIgnore] public DateTimeOffset? NotAfter { get; init; }

    public static JsonWebKeyPublic FromECDsa(ECDsa key, string? kid = null, string? alg = null);
    public static JsonWebKeyPublic FromRsa(RSA key, string? kid = null, string? alg = null);
    /// <summary>RFC 7638 JWK thumbprint (SHA-256, base64url).</summary>
    public string ComputeThumbprint();
}

/// <summary>A JWKS document: <c>{"keys":[...]}</c>.</summary>
public sealed record JsonWebKeySet
{
    /// <summary>The keys in the set.</summary>
    public required IReadOnlyList<JsonWebKeyPublic> Keys { get; init; }
}
```

Thumbprint input: the JCS of the required members only. For EC that is `{"crv","kty","x","y"}`; for RSA, `{"e","kty","n"}`. Build it with `Utf8JsonWriter` in that order (already sorted), then SHA-256, then base64url.

```csharp
public static class JwsAlgorithms
{
    public const string ES256 = "ES256", ES384 = "ES384", PS256 = "PS256", RS256 = "RS256";
    public static IReadOnlyList<string> All { get; } = [ES256, ES384, PS256, RS256];
    public static bool IsSupported(string alg) => All.Contains(alg, StringComparer.Ordinal);
}

public enum JwsVerifyOutcome { Valid, Invalid, KeyNotUsable }

public static class CardJws
{
    /// <summary>Base64url of the JCS form of <c>{"alg","kid","typ":"JOSE"[,"jku"]}</c>.</summary>
    public static string CreateProtectedHeader(string alg, string kid, Uri? jku);
    /// <summary>ASCII bytes of <c>protected + "." + base64url(payload)</c> (RFC 7515 5.1 step 5).</summary>
    public static byte[] CreateSigningInput(string protectedHeader, ReadOnlySpan<byte> payload);
    /// <summary>Checks a JWS signature. IEEE P1363 (r||s) for ECDSA, as JWS requires.</summary>
    public static JwsVerifyOutcome Verify(string alg, JsonWebKeyPublic key, ReadOnlySpan<byte> signingInput, ReadOnlySpan<byte> signature, out string? reason);
}
```

Verify: ES256 needs `kty=EC`, `crv=P-256`, SHA-256. ES384 needs `kty=EC`, `crv=P-384`, SHA-384. RS256 and PS256 need `kty=RSA` with a modulus of at least 2048 bits, SHA-256, with PKCS#1 v1.5 or PSS padding. Import with `ECDsa.Create(ECParameters { Curve, Q })` and `RSA.Create(RSAParameters { Modulus, Exponent })`. `ECDsa.VerifyData` uses IEEE P1363 by default. A `CryptographicException` during import gives `KeyNotUsable`.

```csharp
/// <summary>A key that signs agent cards (and, in package 08, push notifications).</summary>
/// <remarks>After 1.0, new members are virtual with a default body, never abstract (package 02).</remarks>
public abstract class A2ASigningKey
{
    public abstract string KeyId { get; }          // JWS "kid"
    public abstract string Algorithm { get; }      // ES256, ES384, PS256, RS256
    public abstract DateTimeOffset? NotAfter { get; }
    public abstract JsonWebKeyPublic PublicJwk { get; }
    public abstract ValueTask<byte[]> SignAsync(ReadOnlyMemory<byte> signingInput, CancellationToken ct);
}

/// <summary>Where the server gets its card signing keys.</summary>
/// <remarks>Implementer-facing: after 1.0 a new member gets a default implementation (package 02).</remarks>
public interface IAgentCardSigningKeySource
{
    /// <summary>Keys to sign with right now. Usually one; two during rotation.</summary>
    ValueTask<IReadOnlyList<A2ASigningKey>> GetSigningKeysAsync(CancellationToken ct);

    /// <summary>Public keys to publish in the card JWKS: signing keys, keys published ahead of use, recently retired keys (P3).</summary>
    async ValueTask<IReadOnlyList<JsonWebKeyPublic>> GetPublishedKeysAsync(CancellationToken ct)
        => [.. (await GetSigningKeysAsync(ct).ConfigureAwait(false)).Select(k => k.PublicJwk)];
}
```

- [ ] **Step 4: Run and see them pass.**

- [ ] **Step 5: Commit.** `feat(signing): JWK, JWS primitives and signing key abstractions`

---

### Task 6: Local keys

**Files:**
- Create: `src/Ambinder.A2A.Protocol/Signing/{LocalSigningKey.cs,StaticSigningKeySource.cs}`
- Test: `tests/Ambinder.A2A.Protocol.Tests/Signing/LocalSigningKeyTests.cs`

- [ ] **Step 1: Write the failing tests.**
  - `LocalSigningKey.FromECDsa(ecdsa)`: algorithm ES256 for P-256 and ES384 for P-384. `KeyId` defaults to the RFC 7638 thumbprint; an explicit `kid` wins. `SignAsync` output verifies with `CardJws.Verify`.
  - `FromRsa(rsa)`: RS256 by default; `algorithm: "PS256"` is accepted; a 1024-bit key throws `ArgumentException` with "at least 2048 bits".
  - `FromPemFile(path)`: reads a PKCS#8 or SEC1 EC private key, or a PKCS#8 or PKCS#1 RSA private key (`ImportFromPem`). A PEM with only a public key throws `ArgumentException`: "The PEM file '<path>' has no private key. Card signing needs the private key; use a key file, not a certificate or public key."
  - `FromCertificate(X509Certificate2)`: uses `GetECDsaPrivateKey()` or `GetRSAPrivateKey()`. With no private key it throws (same style of message). `NotAfter` defaults to the certificate's `NotAfter`.
  - An unsupported algorithm (for example `HS256` or `none`) throws: "Algorithm 'HS256' is not allowed for card signing. Use ES256 (default), ES384, PS256 or RS256."
  - `new StaticSigningKeySource(key1, key2).GetSigningKeysAsync()` returns both keys in order.

- [ ] **Step 2: Run, see it fail. Step 3: Implement.** `LocalSigningKey` is a `sealed` class deriving from `A2ASigningKey`, with static factories and an optional `notAfter` parameter. It owns the `ECDsa`/`RSA` instance and implements `IDisposable`. The factories clone the key material with `ExportPkcs8PrivateKey`/`ImportPkcs8PrivateKey`, so the caller may dispose its own instance. `StaticSigningKeySource` is a public sealed class implementing `IAgentCardSigningKeySource`.

- [ ] **Step 4: Run, see it pass. Step 5: Commit.** `feat(signing): local signing keys (ECDsa, RSA, PEM, X509)`

---

### Task 7: B13 ETag helper

**Files:**
- Create: `src/Shared/AgentCardETag.cs` (internal), linked into `Ambinder.A2A.Server` and `Ambinder.A2A.AspNetCore` with `<Compile Include="../Shared/AgentCardETag.cs" Link="Shared/AgentCardETag.cs" />` (match how 01 links the other `src/Shared` files). If package 03 already created a B13 helper, replace it with this one, so there is one implementation with the P6 bytes.
- Test: `tests/Ambinder.A2A.Server.Tests/Signing/AgentCardETagTests.cs`

- [ ] **Step 1: Failing tests.**
  - The same payload with key ids `["b","a"]` and `["a","b"]` gives the same ETag (sorted).
  - A different key id set gives a different ETag.
  - An empty key id list gives `"` + base64url(SHA-256(payload)[0..16]) + `"`. Assert this against a value computed in the test with `SHA256.HashData` and `Base64Url`.
  - The value is strong: it starts with `"`, not `W/`.

- [ ] **Step 2: Implement (P6).**

```csharp
internal static class AgentCardETag
{
    /// <summary>B13: strong ETag over the unsigned canonical card payload plus the sorted signing key ids.</summary>
    public static string Compute(ReadOnlySpan<byte> payload, IEnumerable<string> keyIds)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(payload);
        foreach (var kid in keyIds.Order(StringComparer.Ordinal))
        {
            hash.AppendData("\n"u8);
            hash.AppendData(Encoding.UTF8.GetBytes(kid));
        }
        Span<byte> digest = stackalloc byte[32];
        hash.GetHashAndReset(digest);
        return $"\"{Base64Url.EncodeToString(digest[..16])}\"";
    }
}
```

- [ ] **Step 3: Run, pass, commit.** `feat(signing): card ETag rule B13`

---

### Task 8: Server signing service and registration

**Files:**
- Create: `src/Ambinder.A2A.Server/Signing/{CardSigningOptions.cs,CardSigningServiceCollectionExtensions.cs,DevelopmentCardSigningKeySource.cs,IAgentCardSigner.cs,SignedAgentCard.cs,SigningKeyCache.cs,AgentCardSigner.cs,CardSigningStartupCheck.cs}`
- Test: `tests/Ambinder.A2A.Server.Tests/Signing/{AgentCardSignerTests.cs,SigningKeyCacheTests.cs,CardSigningRegistrationTests.cs}`

- [ ] **Step 1: Failing tests.** Use a `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`). If the test project does not reference it yet, add it; the version is pinned centrally (the B2 harness from 12 may already have added it).
  - `SigningKeyCache`:
    - Keys with `NotAfter` in the past are skipped.
    - The source is called again only after `KeyRefreshInterval` (default 10 minutes).
    - A source that throws on the first load gives "no keys" and logs an error.
    - A source that throws on a later refresh keeps the last keys and logs a warning (P9).
    - A key that disappears or expires stays in `GetPublishedKeys()` for `RetiredKeyGracePeriod` (default 1 day), then drops out.
  - `AgentCardSigner.SignAsync(JsonObject card)`:
    - With one ES256 key: the result has one `AgentCardSignature`. Its protected header decodes to `alg=ES256`, `kid`, `typ=JOSE`, and `jku` only when `JwksUrl` is set. `CardJws.Verify` over `CreateSigningInput(protected, GetSigningPayload(card))` is `Valid`.
    - With two keys (rotation): two signatures, and both verify.
    - The returned JSON bytes parse, contain `signatures`, and their signing payload equals the payload of the input.
    - A `signatures` member already in the input is replaced, not appended to.
    - ETag = `AgentCardETag.Compute(payload, kids)`.
    - Signing the same card twice with the same keys returns the cached entry (the source and `SignAsync` are not called again; count calls on a fake key). After a key change, the card is signed again and the ETag changes.
    - **Cache key and bound** *(cross-plan review)*: the signer's cache is keyed by (SHA-256 of the canonical signing payload, `KeySetId`), never by agent name, because package 09 builds one card per variant (host, tenant or caller) and signs each one (09 plan Task 10, step 6). Two cards that differ only in a host-specific URL get two entries and two ETags. Two identical payloads share one. The cache is a bounded LRU of 1,000 entries, the same bound as 09's variant cache, so per-host variants cannot grow memory without limit. Test: 1,001 distinct payloads leave at most 1,000 entries (internal count, read through `InternalsVisibleTo` to `Ambinder.A2A.Server.Tests`).
    - No usable key and `FailWhenNoKey = false`: an unsigned result, an error log ("Card signing is configured but no usable key is available ... the card is served unsigned"), and ETag = `Compute(payload, [])`.
    - With `FailWhenNoKey = true`: an `InvalidOperationException`.
  - Registration:
    - `AddA2AServer(...).AddCardSigning(o => o.KeySource = src)` resolves `IAgentCardSigner` (implemented by the internal `AgentCardSigner`).
    - `AddLocalCardSigningKey(ecdsa)` registers a `StaticSigningKeySource` (the last registration wins, `Replace`).
    - `AddDevelopmentCardSigningKey()` with environment `Production` fails host start, with this message: "AddDevelopmentCardSigningKey() is only allowed in the Development environment (current: Production). Use AddLocalCardSigningKey(...) or AddAzureKeyVaultCardSigningKey(...) (package Ambinder.A2A.Azure.KeyVault)."
    - `FailWhenNoKey = true` with an empty source fails host start (P11).
    - A relative `JwksUrl` with an empty `PublicBaseUrl` fails host start with the P10 message.
    - Without `AddCardSigning`, no signer is registered and nothing changes.

- [ ] **Step 2: Run, see it fail.**

- [ ] **Step 3: Implement.**

```csharp
public sealed class CardSigningOptions
{
    /// <summary>The key source. Set it here, or call AddLocalCardSigningKey / AddDevelopmentCardSigningKey / AddAzureKeyVaultCardSigningKey.</summary>
    public IAgentCardSigningKeySource? KeySource { get; set; }
    /// <summary>When set, the card JWKS is served here and its absolute URL is put in the JWS "jku" header. Relative paths need A2A:PublicBaseUrl.</summary>
    public string? JwksUrl { get; set; }
    /// <summary>When true, startup fails if no usable signing key is available. Default false: the card is served unsigned and an error is logged.</summary>
    public bool FailWhenNoKey { get; set; }
    public TimeSpan KeyRefreshInterval { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan RetiredKeyGracePeriod { get; set; } = TimeSpan.FromDays(1);
}
```

Extensions (namespace `Microsoft.Extensions.DependencyInjection`, per 01 naming rules), on the `A2AServerBuilder` that `AddA2AServer` returns:
- `AddCardSigning(Action<CardSigningOptions>? configure = null)`: registers the options, `SigningKeyCache`, `AgentCardSigner` as `IAgentCardSigner` (singletons, `TryAdd`) and the `CardSigningStartupCheck` hosted service. It is idempotent.
- `AddLocalCardSigningKey(string pemPath | X509Certificate2 | ECDsa | RSA, string? kid = null)`: calls `AddCardSigning()`, then `Replace`s the `IAgentCardSigningKeySource` registration with a `StaticSigningKeySource`.
- `AddDevelopmentCardSigningKey()`: registers `DevelopmentCardSigningKeySource` (one ephemeral ES256 key per process, created lazily). The hosted check validates `IHostEnvironment.IsDevelopment()`.

The key source comes from `options.KeySource` if it is set, else from the DI-registered `IAgentCardSigningKeySource`. If neither exists, startup fails: "AddCardSigning() needs a key source. Call AddLocalCardSigningKey(...), AddDevelopmentCardSigningKey() or AddAzureKeyVaultCardSigningKey(...), or set CardSigningOptions.KeySource."

The public seam (P18), in `Ambinder.A2A.Server`:

```csharp
namespace Ambinder.A2A.Server.Signing;

/// <summary>Signs agent cards before they are served, and lists the public keys for the card JWKS.</summary>
/// <remarks>
/// <c>AddCardSigning()</c> registers the default implementation as a singleton. Calls can be concurrent. The card endpoints
/// call <see cref="SignAsync"/> when they build a card, not on every request. Implementer-facing: after 1.0 a new member gets
/// a default implementation (package 02).
/// </remarks>
public interface IAgentCardSigner
{
    /// <summary>Signs a card. Any <c>signatures</c> member in the input is replaced.</summary>
    /// <param name="cardJson">The card JSON after the card contributors ran. It is changed: <c>signatures</c> is set.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The JSON to serve, its signatures and its B13 ETag. Unsigned when no usable key exists and <c>FailWhenNoKey</c> is false.</returns>
    /// <exception cref="InvalidOperationException">No usable key exists and <c>CardSigningOptions.FailWhenNoKey</c> is true.</exception>
    ValueTask<SignedAgentCard> SignAsync(JsonObject cardJson, CancellationToken ct);

    /// <summary>Gets the id of the current signing key set. A cached card is stale when its <see cref="SignedAgentCard.KeySetId"/> differs.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The ordinally sorted key ids joined with a newline; empty when no key is usable.</returns>
    ValueTask<string> GetKeySetIdAsync(CancellationToken ct);

    /// <summary>Gets the keys to publish in the card JWKS: the published keys of the source plus keys retired within the grace period.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The JWKS document.</returns>
    ValueTask<JsonWebKeySet> GetJwksAsync(CancellationToken ct);
}

/// <summary>A card ready to serve.</summary>
public sealed record SignedAgentCard
{
    /// <summary>The card JSON bytes to send, with <c>signatures</c> when the card is signed.</summary>
    public required ReadOnlyMemory<byte> Json { get; init; }

    /// <summary>The signatures in <see cref="Json"/>; empty when the card is unsigned.</summary>
    public required IReadOnlyList<AgentCardSignature> Signatures { get; init; }

    /// <summary>The strong B13 ETag, with quotes.</summary>
    public required string ETag { get; init; }

    /// <summary>The key set id the card was signed with (see <see cref="IAgentCardSigner.GetKeySetIdAsync"/>).</summary>
    public required string KeySetId { get; init; }
}
```

`AgentCardSigner` (internal sealed) implements it:

```csharp
internal sealed class AgentCardSigner(SigningKeyCache keys, IOptions<CardSigningOptions> options, IOptions<A2AServerOptions> server, ILogger<AgentCardSigner> log) : IAgentCardSigner
{
    // Cache per card variant: key = SHA-256 of the canonical payload (bytes as base64) + "|" + key set id.
    public async ValueTask<SignedAgentCard> SignAsync(JsonObject cardJson, CancellationToken ct)
    {
        cardJson.Remove("signatures");
        // AOT-safe, no A2AJsonContext: write cardJson with cardJson.WriteTo(Utf8JsonWriter) into a pooled buffer, then parse it.
        using var doc = /* JsonDocument.Parse(buffer.WrittenMemory) */;
        var element = doc.RootElement;
        var payload = AgentCardCanonicalizer.GetSigningPayload(element);
        var set = await keys.GetUsableAsync(ct).ConfigureAwait(false);           // (IReadOnlyList<A2ASigningKey> Keys, string Id)
        // cache lookup -> return when hit
        var signatures = new List<AgentCardSignature>(set.Keys.Count);
        foreach (var key in set.Keys)
        {
            var header = CardJws.CreateProtectedHeader(key.Algorithm, key.KeyId, JkuUri());
            var sig = await key.SignAsync(CardJws.CreateSigningInput(header, payload), ct).ConfigureAwait(false);
            signatures.Add(new AgentCardSignature { Protected = header, Signature = Base64Url.EncodeToString(sig) });
        }
        if (signatures.Count > 0) cardJson["signatures"] = /* JsonArray of {"protected","signature"} */;
        var json = /* write cardJson with Utf8JsonWriter (AOT-safe), no indentation */;
        var etag = AgentCardETag.Compute(payload, set.Keys.Select(k => k.KeyId));
        // store and return a SignedAgentCard
    }

    public async ValueTask<string> GetKeySetIdAsync(CancellationToken ct) => (await keys.GetUsableAsync(ct).ConfigureAwait(false)).Id;

    public async ValueTask<JsonWebKeySet> GetJwksAsync(CancellationToken ct) => /* source.GetPublishedKeysAsync ∪ keys.RetiredWithinGrace, distinct by kid */;
}
```

Write the `JsonObject` with `cardJson.WriteTo(Utf8JsonWriter)` and parse the bytes with `JsonDocument.Parse`; this is AOT-safe and needs no `A2AJsonContext` (which is internal to Protocol once 02 lands). Bound the cache to the number of distinct card variants: keep at most 64 entries and evict the oldest.

Key set id: the ordinally sorted kids joined with `\n`.

Signing happens once per card variant and key set. The payload is computed on each call, which costs one canonicalization per card **build**. Task 9 calls the signer only when the card bytes are built, so steady-state requests do not reach it.

- [ ] **Step 4: Run, see it pass. Step 5: Commit.** `feat(signing): server card signer, key cache and registration`

---

### Task 9: Serve signed cards (public and extended) with the B13 ETag

**Check first:** read Task 0's answer for `IAgentCardContributor`.

**Branch A: package 03's card pipeline exists.**
- Find where 03 builds the per-version card bytes, after the contributors run (`git grep -n "IAgentCardContributor" -- src`).
- Right after the contributors run, call `IAgentCardSigner.SignAsync(cardJson)` if one is registered (`RequestServices.GetService<IAgentCardSigner>()`). Cache `SignedAgentCard.Json` and `SignedAgentCard.ETag` in place of 03's bytes and ETag, and rebuild the entry when `GetKeySetIdAsync` no longer equals `SignedAgentCard.KeySetId`.
- 03's ETag code must use `AgentCardETag.Compute(payload, kids)` (the linked `src/Shared` helper from Task 7), which is the B13 rule 03 defines. If 03 computed its own hash, replace it with `AgentCardETag`, so there is one implementation.
- The extended card goes through the same path, with its own cache entry.
- **If package 09's per-request card pipeline has landed** (09 plan Task 10, `AgentCardBuilderPipeline` and `AgentCardVariantCache`), it replaced 03's single cached card. Call `IAgentCardSigner.SignAsync` at its step 6 ("Sign, once per variant"), after the contributors, and store `SignedAgentCard.Json` and `ETag` in 09's variant cache entry. Do not add a second card cache here. *(cross-plan review)*

**Branch B: 03's pipeline is not there.**

**Files:**
- Modify: `src/Ambinder.A2A.AspNetCore/<Hosting>/AgentCardEndpoints.cs` (the post-01 location of today's `src/A2A.Server/Hosting/AgentCardEndpoints.cs`)
- Modify: `src/Ambinder.A2A.Server/Tasks/TaskManager.cs` (`GetExtendedAgentCardAsync`, today `src/A2A.Server/Tasks/TaskManager.cs:209-220`)
- Test: `tests/Ambinder.A2A.Integration.Tests/Signing/SignedCardEndpointTests.cs`; update `tests/Ambinder.A2A.Integration.Tests/Conformance/AgentCardCachingTests.cs` if it needs to

- [ ] **Step 1: Failing tests** (both branches, via the in-memory host the integration tests use):
  - Signing configured with a fixed test ES256 key:
    - `GET /.well-known/agent-card.json` has `signatures` with one entry that verifies against the key's JWK over `GetSigningPayload(body)`.
    - The per-agent card route is also signed.
    - The extended card (JSON-RPC `GetExtendedAgentCard` and the REST extended card route) is signed and verifies.
    - The RS256 key variant also verifies.
    - The `ETag` header equals `AgentCardETag.Compute(GetSigningPayload(body), [kid])`.
  - Two separate host instances with the same key give the same `ETag` (B13, acceptance).
  - After a re-sign (new process, same key) the `ETag` is unchanged, even though the ECDSA signature bytes differ.
  - A key rotation (a fake source switches keys, `KeyRefreshInterval` passes on the `FakeTimeProvider`) changes the `ETag`.
  - Without signing: no `signatures`, and `ETag` = `Compute(payload, [])`. The existing caching tests (304, `Vary`, `Cache-Control`) still pass.

- [ ] **Step 2: Run, see it fail.**

- [ ] **Step 3: Implement (Branch B).**
  - Change the endpoint's `ConditionalWeakTable<AgentCard, CachedCard>`, so the value holds `SignedAgentCard` and is built asynchronously. Use a `Lazy<Task<SignedAgentCard>>`-style entry. Rebuild it when `IAgentCardSigner.GetKeySetIdAsync` no longer equals the entry's `KeySetId`. Build with `JsonSerializer.SerializeToNode(card, A2AJson.AgentCard)` (or the `A2AJson.Options` fallback from the global constraints) as a `JsonObject`, then `signer.SignAsync`. Without a signer, use `AgentCardCanonicalizer.GetSigningPayload` for the ETag, and the serializer bytes for the body.
  - In `TaskManager.GetExtendedAgentCardAsync` (in `Ambinder.A2A.Server`, so it may use the internal signer directly or the interface), when a signer is registered, return `card with { Signatures = signed.Signatures }`. Its JSON form is the same model, so the payload matches on JSON-RPC and REST. P16 covers gRPC.
  - Update the "Card caching" row in `docs/spec-decisions.md` (Task 19 does the wording).

- [ ] **Step 4: Run all tests and see them pass. Step 5: Commit.** `feat(signing): serve signed public and extended cards with the B13 ETag`

---

### Task 10: Card JWKS endpoint

**Files:**
- Create: `src/Ambinder.A2A.AspNetCore/Signing/CardJwksEndpoint.cs`
- Modify: `src/Ambinder.A2A.AspNetCore/<Hosting>/A2AEndpointRouteBuilderExtensions.cs` (`MapA2A`)
- Test: `tests/Ambinder.A2A.Integration.Tests/Signing/CardJwksEndpointTests.cs`

- [ ] **Step 1: Failing tests.**
  - `JwksUrl = "/.well-known/a2a-card-jwks.json"` with `PublicBaseUrl` set: `GET` that path → 200, `application/json`, `Cache-Control: public, max-age=3600`, and a body of `{"keys":[...]}` with the public JWK of every current key. No `d` member appears anywhere in the body.
  - The `jku` in the signed card's header equals `PublicBaseUrl` + the path.
  - After rotation, the old key is still listed for `RetiredKeyGracePeriod`, then removed (`FakeTimeProvider`).
  - A source with a `GetPublishedKeysAsync` override (a pre-published next key) has that key in the JWKS.
  - `JwksUrl` absolute (`https://keys.example.com/jwks.json`): nothing is mapped, and `jku` is that URL.
  - `JwksUrl` null: no route and no `jku`.
  - The JWKS path does not collide with the push JWKS path (`/.well-known/a2a-push-jwks.json`, package 08).

- [ ] **Step 2: Fail. Step 3: Implement.** `MapA2A` maps `GET {JwksUrl}` anonymously when an `IAgentCardSigner` is registered and `JwksUrl` starts with `/`. The handler writes `IAgentCardSigner.GetJwksAsync` with `A2AJson.JsonWebKeySet`. **Step 4: Pass. Step 5: Commit.** `feat(signing): card JWKS endpoint`

---

### Task 11: Client verification core (pinned keys)

**Files:**
- Create: `src/Ambinder.A2A.Client/Signing/{CardSignaturePolicy.cs,CardVerificationOptions.cs,CardVerificationStatus.cs,CardVerificationResult.cs,IAgentCardVerifier.cs,DefaultAgentCardVerifier.cs,A2ACardVerificationException.cs}`
- Test: `<client tests>/Signing/DefaultAgentCardVerifierTests.cs`

- [ ] **Step 1: Failing tests.** Test cards are signed with `AgentCardSigner`'s code path. Reuse Protocol's `CardJws` with test keys; do not depend on Server. One test per status:
  - `Verified`: a pinned ES256 key, matched by kid. A pinned key without `kid` matches by thumbprint (P14). With two signatures, one by an unknown key and one by a pinned key, the result is still `Verified`.
  - `NotSigned`: no `signatures` member, or an empty array.
  - `Malformed`:
    - the body is not JSON
    - `signatures` is not an array
    - `protected` is not base64url, or decodes to something that is not a JSON object
    - `alg` or `kid` is missing
    - `crit` is present (P8)
    - `signature` is not base64url
  - `AlgorithmNotAllowed`:
    - header `alg` is `HS256` or `none`
    - the alg is supported but not in `AllowedAlgorithms` (remove `RS256` from the set, sign RS256)
    - the key does not fit the alg (P8)
  - `UnknownKey`: kid not pinned, and no JWKS or `jku` source.
  - `KeyExpired`: the pinned key has `NotAfter` < now (P1).
  - `BadSignature`: one byte of `description` changed after signing (the tampered card is rejected); the signature bytes corrupted.
  - `NotChecked`: `Policy = Off` (P2). The verifier does not even parse the body.
  - Overall status when nothing verifies follows the P5 order (a test with one `UnknownKey` signature and one `BadSignature` signature gives `BadSignature`).
  - The result carries `KeyId` of the deciding signature, and a `Message` that names the card URL.
  - Unknown top-level fields from a "newer agent" do not break verification: the signer signed them, so they are in the payload (Goal 4).

- [ ] **Step 2: Fail. Step 3: Implement.**

```csharp
public enum CardSignaturePolicy { Off, IfPresent, Required }

public sealed class CardVerificationOptions
{
    public CardSignaturePolicy Policy { get; set; } = CardSignaturePolicy.IfPresent;
    public ISet<string> AllowedAlgorithms { get; } = new HashSet<string>(StringComparer.Ordinal) { "ES256", "ES384", "PS256", "RS256" };
    public IList<JsonWebKeyPublic> TrustedKeys { get; } = [];
    public IList<Uri> TrustedJwksUrls { get; } = [];
    public bool TrustSameOriginJku { get; set; } = true;
    /// <summary>Until A2A #2122 is settled (C4): a card signed in a known non-spec canonical form is accepted with a warning.</summary>
    public bool AcceptKnownCanonicalizationVariants { get; set; } = true;
}

public enum CardVerificationStatus
{
    Verified, NotSigned, UnknownKey, UntrustedKeySource, AlgorithmNotAllowed,
    BadSignature, CanonicalizationMismatch, KeyExpired, Malformed,
    /// <summary>Policy is Off; nothing was checked (P2).</summary>
    NotChecked,
}

public sealed record CardVerificationResult
{
    /// <summary>The outcome.</summary>
    public required CardVerificationStatus Status { get; init; }

    /// <summary>The key id of the signature that decided the outcome, when there is one.</summary>
    public string? KeyId { get; init; }

    /// <summary>What happened and what to do about it.</summary>
    public required string Message { get; init; }
}

/// <remarks>Implementer-facing: after 1.0 a new member gets a default implementation (package 02).</remarks>
public interface IAgentCardVerifier
{
    ValueTask<CardVerificationResult> VerifyAsync(ReadOnlyMemory<byte> cardJson, Uri cardUrl, CancellationToken ct);
}

public sealed class A2ACardVerificationException : A2AException
{
    public CardVerificationStatus Status { get; }
    public string? KeyId { get; }
    public Uri CardUrl { get; }
    // ctor(CardVerificationResult result, Uri cardUrl): message = result.Message
}
```

Choose `A2AException`'s error code by looking at the existing constructors; use `A2AErrorCode.InvalidAgentResponse` if it fits. Name the choice in the PR body.

`DefaultAgentCardVerifier` flow, per signature: decode the header, check `alg` against `AllowedAlgorithms`, look up the key (pinned only in this task; Task 12 adds JWKS), check expiry, then `CardJws.Verify` over `CreateSigningInput(protected, specPayload)`. The spec payload is computed once per card. Task 13 adds the variant step after `BadSignature`.

Required message texts (tests assert the quoted parts):
- `UnknownKey`: "The card at {url} is signed with key '{kid}', which is not trusted. Add the key to CardVerificationOptions.TrustedKeys or its JWKS URL to CardVerificationOptions.TrustedJwksUrls."
- `BadSignature`: "The card at {url} was changed after signing (signature by key '{kid}' does not match)."
- `AlgorithmNotAllowed`: "The card at {url} is signed with algorithm '{alg}', which is not allowed. Allowed: {list}."
- `KeyExpired`: "The card at {url} is signed with key '{kid}', which expired at {notAfter:O}. Ask the agent owner to rotate the key, or update TrustedKeys."
- `Malformed`: "The card at {url} has a malformed signature: {detail}."
- `NotSigned`: "The card at {url} is not signed."
- `Verified`: "The card at {url} is signed by key '{kid}' and the signature is valid."

- [ ] **Step 4: Pass. Step 5: Commit.** `feat(signing): client card verifier with pinned keys`

---

### Task 12: JWKS key sources (`TrustedJwksUrls`, same-origin `jku`)

**Files:**
- Create: `src/Ambinder.A2A.Client/Signing/JwksCache.cs` (internal)
- Modify: `DefaultAgentCardVerifier.cs`
- Test: `<client tests>/Signing/JwksKeyLookupTests.cs`

- [ ] **Step 1: Failing tests.** Use a fake `HttpMessageHandler` behind an `IHttpClientFactory` stub, with `https://` URLs.
  - Lookup order: `TrustedKeys`, then `TrustedJwksUrls`, then `jku` (assert with a handler that counts calls; a pinned hit makes no HTTP call).
  - A key found in a `TrustedJwksUrls` document gives `Verified`.
  - `jku` on the same scheme, host and port as the card URL gives `Verified`.
  - `jku` on another host, another port or `http` gives `UntrustedKeySource` (acceptance: "`jku` on another origin not trusted"). The other host must receive **no** request.
  - `TrustSameOriginJku = false`: a same-origin `jku` gives `UntrustedKeySource` without fetching.
  - A JWKS response over 64 KB (by `Content-Length`, and by a chunked body with no length) gives `UnknownKey`, with "larger than 64 KB" in the message (acceptance: size limit).
  - A JWKS fetched once is not fetched again within 1 hour (`FakeTimeProvider`), and is fetched again after it.
  - A failed fetch (HTTP 500, invalid JSON) gives `UnknownKey` with the reason, and the next fetch waits at least 1 minute (P13).
  - The JWKS request uses the named client `"Ambinder.A2A.CardJwks"`.

- [ ] **Step 2: Fail. Step 3: Implement.** `JwksCache(IHttpClientFactory, TimeProvider)`. Read the body through a bounded loop with a limit of 64 × 1024 + 1 bytes. Parse with `A2AJson.JsonWebKeySet`. Cache per URL: success for 1 hour, failure for 1 minute, at most 256 URLs (evict the oldest). Same-origin means `Uri.Compare(a, b, UriComponents.SchemeAndServer | UriComponents.Port, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0` and `jku.Scheme == "https"`. **Step 4: Pass. Step 5: Commit.** `feat(signing): JWKS key lookup with origin rule and size limit`

---

### Task 13: Canonicalization variants and `CanonicalizationMismatch`

**Files:**
- Modify: `DefaultAgentCardVerifier.cs`
- Test: `<client tests>/Signing/CanonicalizationVariantTests.cs`

- [ ] **Step 1: Failing tests.** Build test cards signed over `GetSigningPayload(card, CanonicalizationProfile.DropRequiredDefaults)` and over `Python112`. The cards must have a REQUIRED field at its default value, or the two forms are equal.
  - Strict (`AcceptKnownCanonicalizationVariants = false`) gives `CanonicalizationMismatch`. The message names the variant: "The card at {url} is signed over a non-spec canonical form ({variant}), so this is an SDK difference, not tampering. See A2A issue #2122. Set CardVerificationOptions.AcceptKnownCanonicalizationVariants = true to accept it." (Acceptance.)
  - Default (C4) gives `Verified`, with a message that says "accepted known canonicalization variant {variant}", and one warning log.
  - A tampered card that matches no form gives `BadSignature`, never `CanonicalizationMismatch` or `Verified`. Variants are tried only with the same key, and only the two named profiles (anti-goal: no "try many forms").
  - A card whose spec payload equals the variant payload (no REQUIRED defaults) gives `BadSignature` after tampering, and variant computation is skipped when the payloads are byte-identical.

- [ ] **Step 2: Fail. Step 3: Implement.** After `BadSignature` with a found key, compute (lazily, once per card) the `DropRequiredDefaults` and `Python112` payloads and try each one. **Step 4: Pass. Step 5: Commit.** `feat(signing): diagnose known canonicalization variants`

---

### Task 14: Policy, logging, metric, DI and resolver integration

**Files:**
- Create: `src/Ambinder.A2A.Client/Signing/CardVerificationPolicyEnforcer.cs` (internal)
- Modify: `src/Ambinder.A2A.Protocol/A2ATelemetryNames.cs` (`ClientMeter = "Ambinder.A2A.Client"` if it is not there yet; `CardVerifications = "a2a.client.card_verifications"`; `Status = "status"`)
- Branch A or B files below
- Test: `<client tests>/Signing/{CardVerificationPolicyTests.cs,ResolverVerificationTests.cs}`

- [ ] **Step 1: Failing tests (policy), one per policy and status** (acceptance: "one test per `CardVerificationStatus` value and per policy"). This is a table-driven theory over (policy × status) → accept, warn or reject, per spec 04 "Policy results":

| Status | Off | IfPresent | Required |
|---|---|---|---|
| NotChecked | accept | n/a | n/a |
| Verified | n/a | accept | accept |
| NotSigned | n/a | accept | reject |
| UnknownKey, UntrustedKeySource | n/a | accept + warning once per card URL | reject |
| BadSignature, AlgorithmNotAllowed, KeyExpired, Malformed, CanonicalizationMismatch | n/a | reject | reject |

  - Reject throws `A2ACardVerificationException`, whose `Status` and message come from the result.
  - The "warning once per card URL" test calls twice with the same URL and expects one log; a second URL gets its own log.
  - Metric: each `VerifyAsync` adds 1 to `a2a.client.card_verifications` with tag `status` = the enum name. Use the repo's existing `MetricCollector` test helper (`tests/Shared/MetricCollector.cs`).

- [ ] **Step 2: Fail. Step 3: Implement the enforcer.** Logging uses `LoggerMessage` source generation, with fixed event ids in the `Ambinder.A2A.Client` block (4000–4999 in the 02 plan, or the scheme in `docs/policies/versioning.md` if it exists). Take ids that no other client log message uses (`git grep -n "EventId" -- src/Ambinder.A2A.Client`). The warned-URL set is bounded (1024 entries).

- [ ] **Step 4: Wire it in.** Check Task 0's answer for `interface IA2ACardResolver`. Package 10's plan (`docs/superpowers/plans/2026-11-03-sdk-10-client.md`) owns the resolver, `ResolvedAgentCard`, `A2AClient` and `AddA2AClient`. This step only calls into them, and never defines 10's types. 10's plan follows the same rule in reverse ("Do not define 04's types here").

  **Branch A: package 10 has landed** (10's Tasks 11, 12 and 17).

  Files:
  - `src/Ambinder.A2A.Client/Cards/A2ACardResolver.cs`
  - `Cards/A2ACardResolverOptions.cs`
  - `Cards/ResolvedAgentCard.cs`
  - `A2AClient.cs`
  - `DependencyInjection/A2AClientServiceCollectionExtensions.cs`

  Changes:
  - **Options home.** Add `public CardVerificationOptions Verification { get; } = new();` to `A2ACardResolverOptions`. Non-DI callers set it through `A2AClientOptions.CardResolver` (10's Task 12), for example `o.CardResolver.Verification.Policy = CardSignaturePolicy.Required`. In DI, find how 10 passes `A2ACardResolverOptions` to its single per-provider `IA2ACardResolver` (`git grep -n "A2ACardResolverOptions" -- src`) and use the same route. Do not add a second options path for verification. The card cache is per provider (10's Task 17), so verification settings are per provider too.
  - **DI** (inside `AddA2AClient`, next to 10's `TryAddSingleton<IA2ACardResolver, A2ACardResolver>`):
    - `TryAddSingleton<IAgentCardVerifier, DefaultAgentCardVerifier>`. A team's own `services.AddSingleton<IAgentCardVerifier, X>()` wins in either order (principle 5).
    - `services.AddHttpClient("Ambinder.A2A.CardJwks")` with a 10-second timeout and a primary `SocketsHttpHandler` that has `AllowAutoRedirect = false` and `UseCookies = false`. No redirects matters for security: a redirect would move a same-origin `jku` fetch to another origin.
  - **Resolver.** The constructor takes `IAgentCardVerifier`. In 10's fetch path:
    1. Read the body with 10's `BoundedContent`.
    2. Call `VerifyAsync(bytes, fetchedUrl)`. `fetchedUrl` is the URL actually fetched, which is the legacy `/.well-known/agent.json` when 10's probe was used.
    3. Run the enforcer. A rejected card throws here and is **not** cached.
    4. Run 10's card readers.
    5. Store the result in the cache entry.

    A 304 revalidation keeps the cached `Verification` and does not verify again, because the bytes did not change.
  - **`ResolvedAgentCard.Verification` becomes non-nullable**, as 10's Task 11 says ("nullable only until 04 lands"). `Policy = Off` gives `NotChecked` (P2), so it always has a value.
  - **`A2ACardResolver.Default`** (non-DI) uses a `DefaultAgentCardVerifier` with default options. Its JWKS fetches use an `HttpClient` over `NonDisposingHandler(A2AClient.SharedHandler)` (10's Task 17), whose `AllowAutoRedirect = false` already applies.
  - **`A2AClient.CardVerification`** keeps 10's type, `CardVerificationResult?`. `EnsureCardAsync` sets it from `ResolvedAgentCard.Verification`. It stays `null` before the card is fetched, and for `A2AClient.Create(card)`, which fetches nothing. Document both cases in its XML doc.
  - **Errors.** `A2ACardVerificationException` comes out of `ConnectAsync`, `CreateClientAsync` and a lazy first call unchanged. It is not wrapped in `A2ATransportException`, and 10's resilience never retries it, because it is not a transport error.
  - **Turn on 10's skipped test** `Verifier_runs_on_raw_bytes_and_result_is_kept` in `tests/Ambinder.A2A.Client.Tests/CardResolverTests.cs`: remove `Skip = "needs package 04"` and implement it.

  **Branch B: package 10 has not landed.** Change the existing resolver only. Leave the types and DI to 10.
  - In `src/Ambinder.A2A.Client/A2ACardResolver.cs` (post-01 path), add an optional constructor parameter `IAgentCardVerifier? verifier = null`. With no verifier, create a `DefaultAgentCardVerifier` with default options (`IfPresent`). Its JWKS fetches use the resolver's `HttpClient` through a one-client `IHttpClientFactory` adapter.
  - Verify the raw bytes before deserializing, then run the enforcer. A rejected card throws and is not cached.
  - Do **not** add `ResolvedAgentCard`, `ResolveAsync` or any DI method. 10's Task 0 will find `interface IAgentCardVerifier`, and its Task 11 then calls the verifier itself.
  - Package 10's plan already has these steps, marked **(04)** in its Tasks 11, 12 and 17. List them in the PR body as the hand-off, so the person running package 10 can check them:
    - **10 Task 11:** make `ResolvedAgentCard.Verification` non-nullable, add `A2ACardResolverOptions.Verification`, keep the cached verification on 304, do not cache a rejected card, and pass the actually fetched URL.
    - **10 Task 12:** set `A2AClient.CardVerification` in `EnsureCardAsync`.
    - **10 Task 17:** register `TryAddSingleton<IAgentCardVerifier, DefaultAgentCardVerifier>` and the `"Ambinder.A2A.CardJwks"` named client (no redirects).
    - **All of 10:** do not wrap or retry `A2ACardVerificationException`.

  **Both branches: extended card.** 10's Task 18 fetches the extended card through the `GetExtendedAgentCard` operation, not through the resolver. Neither spec 04 nor spec 10 says it must be verified, so this task does not verify it. The owner decided on 2026-11-03 that the client does not verify the extended card. Do not add it here.

- [ ] **Step 5: Resolver tests.** Branch A: put them in `tests/Ambinder.A2A.Client.Tests/Signing/ResolverVerificationTests.cs`, using 10's `StubHandler`, `FakeTimeProvider` and its loopback host (or the B2 harness), and call `IA2ACardResolver.GetAsync`. Branch B: put them in the client test location from the file map, and call `GetAgentCardAsync`.
  - Both branches:
    - The verifier receives the **raw** response bytes. A fake verifier records them; assert they are byte-equal to what the server sent, including member order and whitespace.
    - A tampered card throws `A2ACardVerificationException` with status `BadSignature`, not wrapped. It is not cached: the next call fetches again, and `StubHandler` counts one request per call, so there is no retry.
    - `Required` with an unsigned card throws.
    - The end-to-end test (server from Task 9 with the JWKS from Task 10, client with `TrustSameOriginJku`) gives `Verified` with no pinned keys. Use https URLs through the test handler, or pin the JWKS URL if the test host is http-only.
  - Branch A only:
    - An unsigned card under the default options resolves with `ResolvedAgentCard.Verification.Status == NotSigned`, and after the first call `A2AClient.CardVerification` has the same result.
    - `Policy = Off` gives `NotChecked`.
    - ETag revalidation with a 304 calls the verifier once in total and keeps the result.
    - After 10's legacy probe (404, then `/.well-known/agent.json`), the verifier receives the `agent.json` URL.
    - `A2AClient.Create(card)` has `CardVerification == null`.
    - A `jku` JWKS URL that answers 302 to another host gives `UnknownKey`, and the redirect target receives no request.
    - `ConnectAsync` on a tampered card throws `A2ACardVerificationException`, not `A2ATransportException`.
  - Branch B only: an unsigned card under the default options resolves.

- [ ] **Step 6: Pass. Step 7: Commit.** `feat(signing): verification policy, metric and resolver integration`

---

### Task 15: Test vectors folder (`ours/`, `python/`, `js/`) and interop status tests

**Files:**
- Create the layout:

```
tests/vectors/card-signing/
  README.md                         one line per vector: path, source, upstream issue (if any)
  jcs/                              (Task 2)
  spec-example/                     (Task 4)
  presence/                         (Task 4)
  ours/
    keys/es256.test-only.pem        TEST-ONLY private keys; README says so
    keys/rs256.test-only.pem
    jwks.json
    full-card.es256.card.json       signed by our server code path
    full-card.rs256.card.json
    full-card.payload.json          expected canonical payload (both cards share it)
  python/  js/
    README.md                       "Produced by package 16 (SIGN-01) at pinned versions. Each <case>.card.json has <case>.jwks.json and <case>.expect.json."
```

- `<case>.expect.json`: `{"status":"Verified|CanonicalizationMismatch|...","form":"spec|DropRequiredDefaults|Python112","sdk":"a2a-python","version":"x.y.z","upstreamIssue":"https://..."}`
- Test: `tests/Ambinder.A2A.Integration.Tests/Signing/CardSigningVectorTests.cs`

- [ ] **Step 1: Generate `ours/`** with an opt-in test that runs only when `A2A_REGENERATE_VECTORS=1`. It loads the test keys, signs `FullCards.EveryField()` through `AgentCardSigner`, and writes the files. Run it once, review the output, and commit. These keys are test-only and are never used outside `tests/`. Say so in `keys/README.md`.
- [ ] **Step 2: Tests.**
  - For each `ours/*.card.json`: the payload equals `full-card.payload.json`, and `DefaultAgentCardVerifier` with the keys from `jwks.json` pinned gives `Verified`.
  - For each `python/*.card.json` and `js/*.card.json`: verify with its JWKS pinned, under strict options **and** under default options. Assert the status in `expect.json`, and that strict and default differ only where the C4 rule says they should.
  - If `python/` or `js/` has no card files, call `Assert.Skip("No Python/JS signed cards yet; package 16 (SIGN-01) produces them.")`. xUnit v3 reports this as skipped, which is visible in the results.
- [ ] **Step 3: If package 16 has landed** (Task 0), run its SIGN-01 harness to fill `python/` and `js/`. For each divergent vector, confirm the `DropRequiredDefaults`/`Python112` definitions against it. If a peer card matches none of our forms, do **not** widen the variant to make it match. Record the vector as `BadSignature`, with the upstream issue link, and list it in the PR body. Our cards verifying in the Python and JS verifiers is 16's SIGN-01 check; if 16 has not landed, list it as a hand-off.
- [ ] **Step 4: Commit.** `test(signing): card-signing test vectors and interop status tests`

---

### Task 16: `Ambinder.A2A.Azure.KeyVault`

**Files:**
- Create: `src/Ambinder.A2A.Azure.KeyVault/*` (see the file map), `tests/Ambinder.A2A.Azure.KeyVault.Tests/*`
- Modify: `Directory.Packages.props`, `Ambinder.A2A.slnx`, `eng/package-dependencies.json`

- [ ] **Step 1: The project.** Package ID, assembly and namespace are `Ambinder.A2A.Azure.KeyVault`, as in 01's map. It references `Ambinder.A2A.Server`, `Azure.Security.KeyVault.Keys` and `Azure.Identity`. AOT flag no. It has a README of about 40 lines in package 13's format: what it is, install, a 10-line example, and the RBAC role **Key Vault Crypto User** (needs key permissions `sign` and `get`).

- [ ] **Step 2: Failing tests** with a fake `IKeyVaultKeyOperations` and a `FakeTimeProvider`:
  - Signing version:
    - The newest enabled version whose `CreatedOn` (or `NotBefore`, whichever is later) is at least `PublishLead` (1 h) in the past.
    - With two versions, where the newer is 10 minutes old: the older one signs, and both are published (acceptance: "a new version is published before it signs"). After the time passes `PublishLead`, the newer one signs.
    - With only one fresh version (first deployment), it signs at once.
  - Published keys: every enabled version, plus versions replaced or expired less than `RetireGrace` (24 h) ago. After `RetireGrace`, a replaced version drops out (acceptance).
  - Disabled versions are never used or published. Expired versions never sign.
  - `kid` = `{keyName}/{version}`. `NotAfter` = `ExpiresOn`.
  - Algorithm per P12: an EC P-256 key signs ES256 with SHA-256, and the fake receives a 32-byte digest. EC P-384 signs ES384 with SHA-384. RSA signs RS256. An `oct` key fails on first load: "Key Vault key '{name}' has type '{kty}', which cannot sign agent cards. Use an EC (P-256 or P-384) or RSA key."
  - `RefreshInterval` (1 h): the versions are listed again only after the interval. If the first refresh fails, the source throws at first use. If a later refresh fails, the source keeps the last keys and logs a warning.
  - The real adapter `AzureKeyVaultKeyOperations`, given a subclass of `CryptographyClient` (Azure SDK clients have protected parameterless constructors and virtual methods for mocking), calls `SignAsync(SignatureAlgorithm.ES256, digest)` with the SHA-256 of the signing input, and returns the signature bytes unchanged (acceptance: "fake `CryptographyClient`"). Build the `KeyProperties`/`KeyVaultKey` test models with `KeyModelFactory` if the pinned SDK version has it. If it does not, test the mapping through the live test only, and say so in the PR body.
  - Default credential (when `credential` is null): `Development` gives `DefaultAzureCredential`. Any other environment gives `ChainedTokenCredential` built from `WorkloadIdentityCredential` and `ManagedIdentityCredential`. Test this through an internal factory that returns a description of the chain, because `ChainedTokenCredential` does not expose its sources.
  - `AddAzureKeyVaultCardSigningKey(new Uri("https://v.vault.azure.net/keys/card"))` registers the source as `IAgentCardSigningKeySource` (`Replace`) and calls `AddCardSigning()`. A key URI that is not a Key Vault key URL throws `ArgumentException`, with an example of the expected form.
  - The source overrides `GetPublishedKeysAsync` (P3), so the card JWKS from Task 10 lists the pre-published and recently retired versions.

- [ ] **Step 3: Fail, implement, pass.**

```csharp
public sealed class AzureKeyVaultKeySourceOptions
{
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan PublishLead { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RetireGrace { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>Key Vault signing keys. The private key never leaves the vault. Used for card signing (04) and push signing (08).</summary>
public sealed class AzureKeyVaultKeySource : IAgentCardSigningKeySource
{
    // public: package 08's AddAzureKeyVaultPushSigningKey builds on GetCurrentSigningKeyAsync + GetPublishedKeysAsync
    public ValueTask<A2ASigningKey> GetCurrentSigningKeyAsync(CancellationToken ct);
    public ValueTask<IReadOnlyList<A2ASigningKey>> GetSigningKeysAsync(CancellationToken ct);   // [current]
    public ValueTask<IReadOnlyList<JsonWebKeyPublic>> GetPublishedKeysAsync(CancellationToken ct);
}

internal interface IKeyVaultKeyOperations
{
    IAsyncEnumerable<KeyVersionInfo> ListVersionsAsync(CancellationToken ct);   // version, enabled, createdOn, notBefore, expiresOn
    ValueTask<JsonWebKeyPublic> GetPublicKeyAsync(string version, CancellationToken ct);
    ValueTask<byte[]> SignDigestAsync(string version, string algorithm, byte[] digest, CancellationToken ct);
}
```

Retired-at time: the moment a version stopped being the signing version, observed at refresh, or its `ExpiresOn`, whichever comes first. A restart forgets "observed" times. That is fine, because the older enabled versions stay published anyway (every enabled version is published).

- [ ] **Step 4: Live test.** `AzureKeyVaultLiveTests` runs only when the environment variables `A2A_TEST_KEYVAULT_KEY_ID` (and any credential variables the test docs name) are set. Otherwise it calls `Assert.Skip("Key Vault live test skipped: set A2A_TEST_KEYVAULT_KEY_ID (owner decision O3).")`. It signs a card, verifies it with `DefaultAgentCardVerifier` against the published JWK, and checks that the JWKS has the current version. It also checks that the EC signature is 64 bytes (r‖s). If it is DER, add a conversion and a unit test for it. Add a line to the manual pre-release checklist (wherever 01 put it; else in the PR body): "Run AzureKeyVaultLiveTests against a real vault: sign, verify, rotate a version, JWKS contents."

- [ ] **Step 5: Commit.** `feat(signing): Azure Key Vault signing key source`

---

### Task 17: The core does not reference Azure

**Files:** `eng/package-dependencies.json` (01). If 01's allow-list test does not cover project-level references, add `tests/Ambinder.A2A.Integration.Tests/Repo/CoreDependencyTests.cs`.

- [ ] **Step 1:** Add the `Ambinder.A2A.Azure.KeyVault` row to 01's allow-list, with `Ambinder.A2A.Server`, `Azure.Security.KeyVault.Keys` and `Azure.Identity`. Run 01's allow-list test.
- [ ] **Step 2:** A test that reads `project.assets.json` (from `obj/`) for `Ambinder.A2A.Protocol`, `Server`, `AspNetCore` and `Client`, and fails on any `Azure.*` or `Microsoft.IdentityModel.*` library in their dependency closure. The failure message names the package and the path. Skip it only if 01's test already checks the same closure; say so in the PR body.
- [ ] **Step 3: Run, pass, commit.** `test(signing): core packages have no Azure dependency`

---

### Task 18: Benchmarks (package 14)

- [ ] **If `benchmarks/Ambinder.A2A.Benchmarks` exists:** add `CardSigningBenchmarks` with `[MemoryDiagnoser]`:
  - `GetSigningPayload` on a card of about 20 KB (`FullCards.EveryField()` with skills repeated until the JSON is about 20 KB; build it in `[GlobalSetup]`).
  - `VerifyAsync` with one pinned ES256 key.

  Run once with the `Short` job, and paste the numbers into the PR body.
- [ ] **Else:** add a "Hand-offs" line to the PR body: "Package 14: add `CardSigningBenchmarks` (`GetSigningPayload` on a 20 KB card; full verify)."
- [ ] Commit, if anything changed: `perf(signing): card signing benchmarks`

---

### Task 19: Docs, spec decisions, sample, API files

**Files:**
- `docs/spec-decisions.md`
- `docs/security/agent-card-trust.md`
- `samples/CardSigning/`
- `PublicAPI.Unshipped.txt` files (if 02 has landed)
- the Protocol, Server, Client and AspNetCore READMEs (one short section each)

- [ ] **Step 1: `docs/spec-decisions.md`.** Update the "Card caching" row's ETag text to B13 and P6. Add rows (spec section, choice, reason):
  - A2A #2122 reading: canonicalize the card as served; REQUIRED kept; the spec example passes; `CanonicalizationProfile` for a later ruling. Link the issue.
  - Number handling: integers outside ±(2^53−1) are an error.
  - Unknown fields kept; `null` on known fields removed (P7); empty message kept as `{}`; REQUIRED beats empty-repeated (P17).
  - Default algorithm ES256. Allowed: ES256, ES384, PS256, RS256. No `none` and no HMAC.
  - Expired keys are skipped; with no key, the card is served unsigned unless `FailWhenNoKey`.
  - `jku` trusted only on the same origin over HTTPS. JWKS limits: 64 KB and 1 h cache (P13).
  - Client default `IfPresent` with known variants accepted (C4), and the `IfPresent` table.
  - Verify-side header rules (P8).
  - Extended card on gRPC (P16).
  - Extended card on the client: the server signs it, but the client does not verify its signature. The client gets it from `GetExtendedAgentCard` over the authenticated connection, not through the card resolver (owner decision, 2026-11-03). Only the public card fetched by the resolver is verified.
- [ ] **Step 2: Sample.** Add `samples/CardSigning/` (two small programs, or one program with two modes). It shows signing with `AddDevelopmentCardSigningKey()` and a client resolving and verifying the card through a same-origin JWKS. It may add at most 10 lines over the plain hello agent and client (principle 3). Add it to the solution. A test in `tests/.../Samples/` runs it in-memory and asserts `Verified`.
- [ ] **Step 3: `docs/security/agent-card-trust.md`.** Follow package 13's page rules: title as an action, Before you start, at most 7 steps, Check it worked, If it fails, Next, and the front matter `packages`, `since`, `verified`, `owner: 04`. It has two sections, "Sign your agent card" and "Verify agent cards". Include:
  - the Key Vault RBAC role
  - the `IfPresent` behavior table from Task 14
  - the strict-mode switch
  - the "If it fails" entries, quoting the exact messages from Tasks 8, 11 and 13

  Any C# block over 5 lines is a DocFX include from `samples/CardSigning`. If the page goes past about 800 words, split it into `sign-your-agent-card.md` and `verify-agent-cards.md`. Add the page to whatever index 13 has. If 13 has not landed, link it from `README.md`'s docs list, so `DocsTests` checks its links.
- [ ] **Step 4: API files.** If `PublicAPI.Unshipped.txt` exists (02), check that every new public member is in it (each task should already have added its own), and that the build has no RS0016 or RS0017. Check XML docs on every public member. If `tests/Ambinder.A2A.PublicApi.Tests` exists, it passes, and `AgentCardSigner`, `SigningKeyCache`, `AgentCardETag`, `KeyVaultSigningKey` and the Key Vault adapter are not public. Check the other package 02 rules in the global constraints for each new public type.
- [ ] **Step 5: Commit.** `docs(signing): card signing guide, spec decisions, sample`

---

### Task 20: Final verification and PR

- [ ] **Step 1:**

```bash
dotnet build Ambinder.A2A.slnx -c Release && dotnet test --solution Ambinder.A2A.slnx -c Release --no-build
```

Expected: green. Skipped tests are only the Key Vault live test and the Python/JS vectors when they are absent, each with its skip message.

- [ ] **Step 2: AOT check** for Protocol and Client, if 01's AOT smoke job exists (run it locally the same way CI does). Expected: no new trim or AOT warnings.
- [ ] **Step 3: Acceptance walk-through.** Copy spec 04's acceptance criteria into the PR body. For each one, give the test name(s) or the hand-off.
- [ ] **Step 4: PR body sections:**
  - Summary
  - Dependency branches taken (Tasks 9 and 14: A or B)
  - Planner's calls P1–P17, for the owner to confirm (P18 is already an owner decision)
  - Hand-offs to 10, 14 and 16, if those packages have not landed
  - Acceptance table
  - Test results

---

## Spec coverage (self-review)

| Spec 04 acceptance criterion | Task |
|---|---|
| `JsonCanonicalizer` passes every RFC 8785 test vector | 2 |
| Field table generated from the proto; a test fails if a card field lacks an entry | 3 |
| Spec 8.4 example gives the expected bytes | 4 |
| Each `presence/` vector gives the expected payload | 4 |
| Server: signed public and extended cards; ES256, RS256; expired key skipped; two keys during rotation; JWKS current and retired | 8, 9, 10 |
| Client: one test per status and per policy; tampered card rejected; other-origin `jku` not trusted; JWKS size limit | 11, 12, 13, 14 |
| `CanonicalizationMismatch` in strict mode; `Verified` with a warning by default (C4) | 13 |
| ETag the same on two instances and after re-signing (B13) | 7, 9 |
| Interop via 16 | 15 (and hand-off) |
| Key Vault: fake `CryptographyClient`, `PublishLead`, `RetireGrace`, credential per environment, live test skipped visibly, checklist line | 16 |
| Core has no Azure reference | 17 |
| Docs section (13 format) | 19 |
| `docs/spec-decisions.md` rows | 19 |
| Goal 9: JCS public and reusable | 1, 2 |
| Principle 4 benchmarks | 18 |

## Execution notes

- **How to run it:** execute this plan with **superpowers:subagent-driven-development**. The controller (the session that runs the plan) dispatches one fresh implementer subagent per task. After each task, it runs that skill's review steps (spec-compliance review, then code-quality review) before the next task starts.
- **Model:** every subagent runs on model **`sonnet`**: implementers, spec reviewers, code-quality reviewers and any fix-up subagent. Pass `model: "sonnet"` explicitly on every Agent/Task dispatch. Do not rely on the default. This is the owner's rule: planning on Opus, execution on Sonnet.
- **Order:** run the tasks in sequence, 0 to 20. Do not run implementers in parallel: most tasks touch `Ambinder.A2A.Protocol/Signing/`, and later tasks build on earlier types. Task 16 (Key Vault) and Task 18 (benchmarks) only need Tasks 5, 6, 8 and 10. The controller may move them later, but not earlier.
- **What to give each subagent:** the full text of its task, the "Global constraints" section, the "Decisions" section (P1–P18) and Task 0's branch answers. Reviewers check the package 02 rules in the global constraints on every task that adds public API, logging or JSON code. Subagents should not need to read the whole plan.
- **Stop and ask the owner** (do not improvise) when:
  - Task 0 finds package 01 has not landed;
  - the spec example or the RFC vectors cannot be fetched;
  - the spec example disagrees with a field rule (P17 or any other);
  - a peer vector matches no known form;
  - a required package version cannot be found as stable;
  - anything in this plan contradicts spec 04 or the work plan. The spec wins; report the contradiction.
- **Commits and PR:** one commit per task, as each task says. When Task 20 is green, open one PR into `master` with the body from Task 20 Step 4. CI must be green before review.
