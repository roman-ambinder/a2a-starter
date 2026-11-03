# Package 04: Agent card signing and verification

Date: 2026-11-03
Status: approved by owner 2026-11-03
Milestone: M1
Size: M

## Intent

An agent card tells a client where to send requests and where to get tokens. A spoofed card can send credentials to the wrong place. The spec lets an agent sign its card with JWS (spec 8.4) and says clients SHOULD verify it. Today the models exist but nothing signs or verifies (R11).

This package adds both sides: the server signs its cards, and the client verifies them under a clear policy. The hard part is canonicalization. SDKs disagree on it today (pain point 5), so a signature from one SDK often fails in another. We ship one RFC 8785 implementation that follows the spec text, test it against the spec example and against cards signed by Python and JS, and make every verify failure say why it failed.

## Findings addressed

- R11: card signatures are modeled but never produced or verified.
- R29 (signature part only): the card resolver has no signature check.
- Pain point 5, "Signed Agent Cards: canonicalization mismatch".
- Pain point 15, "Identity / trust / authorization of agents": a card-verification policy hook, without inventing a trust model.

## Goals

1. A server can sign every card it serves (public and extended) with one or more keys, configured in a few lines.
2. The signature follows spec 8.4: the payload is the card with `signatures` removed and default-valued fields removed, canonicalized with RFC 8785 (JCS); the JWS protected header has `alg` and `kid`, `typ` is `JOSE`, and `jku` is optional.
3. A client verifies card signatures by policy: `Off`, `IfPresent` (default) or `Required`.
4. Verification works on the bytes received, not on a re-serialized model, so unknown fields from newer agents do not break it.
5. Every verify failure has a reason code that separates: no signature, unknown key, untrusted key source, algorithm not allowed, bad signature, and canonicalization mismatch (the signature matches a known non-spec canonical form).
6. The spec's canonicalization example (fixed in A2A PR #2319) is a passing test vector.
7. Cards signed by the Python and JS reference SDKs verify in CI, and cards we sign verify in those SDKs, or each gap is documented with the upstream issue.
8. Key sources are an interface. The core ships a local key source (PEM, PFX, or a generated dev key). A separate package ships an Azure Key Vault key source where the private key never leaves the vault.
9. The JCS implementation is public and reusable, and passes the RFC 8785 test vectors.

## Non-goals

- A trust model for agents (who is allowed to be which agent). Spec does not settle it (pain point 15). We give a policy hook only.
- Signing push notifications. Package 08. It may reuse the key source interface from this package.
- Client card caching, ETag and the extended-card refresh flow. Package 10.
- Card HTTP headers (Cache-Control, ETag, Vary). Package 03.
- Key rotation automation inside Key Vault (rotation policies, alerts). Teams use Key Vault's own features; we only read the current and previous key versions.
- Other key stores (AWS KMS, HSM via PKCS#11). Possible later behind the same interface.

## Anti-goals

- Sign and verify that only work between our own client and server.
- A verifier that accepts a tampered card because it tried many canonical forms until one matched.
- Fetching keys from any `jku` URL found in a card. That lets the attacker supply the key.
- A dependency on Azure SDKs in the core packages.
- Verify on by default in a way that breaks calls to the many agents that sign nothing.

## Constraints

- Spec 8.4 (via `research-spec.md` section 3, "Agent card"): canonicalize with RFC 8785 after removing default-valued fields. Keep REQUIRED fields and explicitly set `optional` fields. Omit empty repeated fields. Drop the `signatures` field. Protected header MUST have `alg` and `kid`; `typ` SHOULD be `JOSE`; MAY have `jku`. Expired or revoked keys MUST NOT be used. Clients SHOULD verify when present.
- Proto: `AgentCardSignature { protected (REQUIRED, base64url JSON), signature (REQUIRED, base64url), header (Struct) }` (`proto/a2a.proto:457-467`); `AgentCard.signatures` is field 13 (`proto/a2a.proto:396`).
- Open spec issue A2A #2122 (48 comments): Python 1.1.2 escapes non-ASCII, sorts by code point and writes `1e-06`; JS follows RFC 8785; both drop REQUIRED fields at default values, which contradicts the spec example (`research-painpoints.md` pain point 5; also a2a-python #1278, TCK #261, #187, #227, #236).
- Work plan: no Azure dependency in the core (principle 5); Entra is the only shipped auth (work plan, "Auth"); .NET 10 only.
- Proto pinned to `v1.0.1` (package 03).

## Design

### Packages

- `Ambinder.A2A.Protocol`: JCS canonicalizer, card canonicalization rules, JWS sign/verify primitives, key interfaces. No new dependencies (uses `System.Security.Cryptography` and `System.Text.Json`).
- `Ambinder.A2A.Server`: signs cards it serves.
- `Ambinder.A2A.Client`: verifies cards it fetches.
- `Ambinder.A2A.Azure.KeyVault` (new): Key Vault key source for card signing, also used by package 08 for push signing. Depends on `Ambinder.A2A.Server`, `Azure.Security.KeyVault.Keys` and `Azure.Identity`. (Name confirmed in package 01's map.)

API rule (package 02): `IAgentCardSigningKeySource` and `IAgentCardVerifier` are implementer-facing; after 1.0 a new member gets a default implementation. `A2ASigningKey` is an abstract class; after 1.0 new members are `virtual` with a default body, never `abstract`.

### Canonicalization

Public types in `Ambinder.A2A.Protocol` (namespace `Ambinder.A2A.Protocol.Signing`, per package 01's naming rule):

```csharp
public static class JsonCanonicalizer          // RFC 8785
{
    public static byte[] Canonicalize(JsonElement value);
    public static void Canonicalize(JsonElement value, IBufferWriter<byte> output);
}

public static class AgentCardCanonicalizer     // spec 8.4 field rules + JCS
{
    /// <summary>The bytes that are signed: card JSON minus signatures and default-valued fields, in JCS form.</summary>
    public static byte[] GetSigningPayload(JsonElement cardJson);
}
```

`JsonCanonicalizer` rules (RFC 8785):
- Object members sorted by UTF-16 code units of the names.
- Strings: only the escapes RFC 8785 requires; non-ASCII written as UTF-8, not `\u` escapes.
- Numbers: ECMAScript `Number.prototype.toString` form for IEEE 754 doubles (so `1e-6` becomes `0.000001`, and `1e21` stays `1e+21`). Integers outside the safe range are an error ("number cannot be represented exactly in JCS"), because JCS cannot keep them. *(writer's call; RFC 8785 says such values should be avoided.)*
- No whitespace.

`AgentCardCanonicalizer` field rules, applied to the JSON tree before JCS:
- Remove the top-level `signatures` member.
- For each known message type in the card (AgentCard, AgentInterface, AgentProvider, AgentCapabilities, AgentExtension, AgentSkill, SecurityScheme and its variants, OAuthFlows and its flows, SecurityRequirement), use a field table that says, per field: REQUIRED, proto3 `optional` (has presence), or plain.
  - REQUIRED: always kept, even at its default (`""`, `false`, `0`). This follows the spec text and example, not Python and JS today.
  - `optional`: kept if present in the JSON, whatever its value (presence means "explicitly set").
  - plain scalar: removed if equal to its default (`""`, `false`, `0`, the zero enum name).
  - repeated or map: removed if empty.
  - message: removed if absent; kept if present (an empty object stays `{}`). *(writer's call; proto3 messages have presence.)*
  - `google.protobuf.Struct` / `Value` (for example `AgentExtension.params`): kept as is; no default removal inside.
- Unknown fields (from a newer spec version): kept as received. We cannot know their default, and signers on a newer version will have kept them if set. *(writer's call)*
- The field table is generated at build time from `proto/a2a.proto` (`field_behavior` and `optional`), not written by hand, so it cannot drift from the pinned proto. A test checks every card field has a table entry. *(writer's call: a small generator in `tools/`, output checked in.)*

Our reading of the open issue (A2A #2122): we canonicalize the card exactly as served, with the rules above. This goes into `docs/spec-decisions.md` (package 03) with a link to the issue. When the TSC rules, we change the rules behind a new `CanonicalizationProfile` value and keep the old one for verification for one major version.

### Signing (server)

```csharp
public interface IAgentCardSigningKeySource
{
    /// <summary>Keys to sign with right now. Usually one; two during rotation.</summary>
    ValueTask<IReadOnlyList<A2ASigningKey>> GetSigningKeysAsync(CancellationToken ct);
}

public abstract class A2ASigningKey
{
    public abstract string KeyId { get; }          // JWS "kid"
    public abstract string Algorithm { get; }      // JWS "alg": ES256, ES384, PS256, RS256
    public abstract DateTimeOffset? NotAfter { get; }
    public abstract JsonWebKeyPublic PublicJwk { get; }
    public abstract ValueTask<byte[]> SignAsync(ReadOnlyMemory<byte> signingInput, CancellationToken ct);
}
```

`JsonWebKeyPublic` is a small public record with the JWK public members (`kty`, `crv`, `x`, `y`, `n`, `e`, `kid`, `alg`, `use`). *(writer's call: our own record, so the Protocol package takes no IdentityModel dependency.)*

Registration:

```csharp
builder.Services.AddA2AServer(...)
    .AddCardSigning(o =>
    {
        o.KeySource = ...;              // or AddLocalCardSigningKey / AddAzureKeyVaultCardSigningKey
        o.JwksUrl = "/.well-known/a2a-card-jwks.json";   // published in "jku" when set
    });
```

- `AddLocalCardSigningKey(pemPath | X509Certificate2 | ECDsa)`: a local key. `AddDevelopmentCardSigningKey()`: an ephemeral ES256 key, only allowed when `IHostEnvironment.IsDevelopment()`; startup fails otherwise with a message that says which method to use. *(writer's call)*
- Default algorithm: ES256. Allowed: ES256, ES384, PS256, RS256. No `none`, no HMAC (a shared secret cannot prove the card's origin). *(writer's call)*
- Keys with `NotAfter` in the past are skipped. If no usable key is left, the server logs an error and serves the card unsigned, unless `o.FailWhenNoKey` is true (default false). *(writer's call; the spec says expired keys MUST NOT be used, it does not require a signature.)*
- When signing happens: once per card version, not per request. The card bytes and signatures are cached together with the ETag from package 03. The ETag is the hash of the unsigned canonical card payload (`GetSigningPayload`) plus the signing key ids, not of the signed bytes (Claude's call, B13, recorded in 03's spec-decisions table). ECDSA signatures differ on each signing, so this keeps the ETag the same across pods and after re-signing with the same keys. A key rotation changes the key ids and so the ETag. Key source results are cached for `o.KeyRefreshInterval` (default 10 minutes).
- Steps per key: serialize the card with the normal serializer, apply the `IAgentCardContributor`s (package 03), parse to `JsonElement`, `AgentCardCanonicalizer.GetSigningPayload`, build the protected header `{"alg","kid","typ":"JOSE"[,"jku"]}`, compute `base64url(header) + "." + base64url(payload)`, sign, add an `AgentCardSignature { Protected, Signature }` to the served card. The payload is not stored in the card (detached), as the proto has no payload field.
- JWKS endpoint (optional, on when `JwksUrl` is set): serves the public keys of all current keys plus keys that expired less than `o.RetiredKeyGracePeriod` ago (default 1 day), so clients with a cached card still verify. `Cache-Control: public, max-age=3600`.
- Extended cards are signed the same way.

### Verification (client)

```csharp
public enum CardSignaturePolicy { Off, IfPresent, Required }

public sealed class CardVerificationOptions
{
    public CardSignaturePolicy Policy { get; set; } = CardSignaturePolicy.IfPresent;
    public ISet<string> AllowedAlgorithms { get; } = { "ES256", "ES384", "PS256", "RS256" };
    public IList<JsonWebKeyPublic> TrustedKeys { get; } = [];      // pinned keys
    public IList<Uri> TrustedJwksUrls { get; } = [];               // pinned JWKS
    public bool TrustSameOriginJku { get; set; } = true;
    public bool AcceptKnownCanonicalizationVariants { get; set; } = true;   // until A2A #2122 is settled (C4)
}

public interface IAgentCardVerifier
{
    ValueTask<CardVerificationResult> VerifyAsync(ReadOnlyMemory<byte> cardJson, Uri cardUrl, CancellationToken ct);
}

public sealed record CardVerificationResult(CardVerificationStatus Status, string? KeyId, string Message);

public enum CardVerificationStatus
{
    Verified, NotSigned, UnknownKey, UntrustedKeySource, AlgorithmNotAllowed,
    BadSignature, CanonicalizationMismatch, KeyExpired, Malformed
}
```

- The card resolver (package 10 owns it) calls `IAgentCardVerifier` with the raw response bytes before it deserializes the model. The default verifier is registered by `AddA2AClient(name, ...)` and used by `A2AClient.ConnectAsync`; teams can replace it (principle 5).
- Key lookup, in order: `TrustedKeys` by `kid`; `TrustedJwksUrls`; then `jku` from the protected header, only if `TrustSameOriginJku` and the `jku` is HTTPS with the same scheme, host and port as the card URL. *(writer's call; this keeps a hostile card from pointing at its own key server, though a same-origin attacker can still sign. Teams that need more pin keys.)*
- JWKS fetches use `IHttpClientFactory`, a 64 KB size limit, and a 1-hour cache.
- A card is `Verified` when at least one signature verifies with a trusted key. Other signatures may fail (rotation).
- Policy results:
  - `Off`: no check.
  - `IfPresent` (default): no signature: accept. A signature whose key cannot be found or trusted (`UnknownKey`, `UntrustedKeySource`): accept, log a warning once per card URL. Any other failure (`BadSignature`, `AlgorithmNotAllowed`, `KeyExpired`, `Malformed`, `CanonicalizationMismatch`): reject. *(writer's call: "present but broken" is a red flag; "present but no key to check" is normal for agents we have no trust in.)*
  - `Required`: anything other than `Verified` rejects.
- Reject means `A2ACardVerificationException : A2AException` with the status and a message that says what to do. Examples: "The card at X is signed with key 'k1', which is not trusted. Add its JWKS URL to CardVerificationOptions.TrustedJwksUrls." / "The card at X was changed after signing (signature does not match)."
- Diagnosing other SDKs: after a `BadSignature`, the verifier also tries the known variant forms (`DropRequiredDefaults`, the Python 1.1.2 form). If one matches, the status is `CanonicalizationMismatch` with the variant name in the message, so the team knows it is an SDK bug and not tampering. It is still a reject, unless `AcceptKnownCanonicalizationVariants` is true; then it is `Verified` with a warning log. This is the "format drift vs tamper" split suggested in pain point 5.
- Default *(Claude's call, C4)*: `Policy = IfPresent` with `AcceptKnownCanonicalizationVariants = true` until A2A #2122 is settled, so cards signed by the Python and JS SDKs in the divergent form are accepted (with a warning). Strict mode is opt-in: set `AcceptKnownCanonicalizationVariants = false`, or `Policy = Required`. When the TSC rules, the default is reviewed in a minor release with a changelog note.
- The result is kept with the card. Package 10 exposes it as `ResolvedAgentCard.Verification` and `A2AClient.CardVerification`.
- Metrics: counter `a2a.client.card_verifications` with tag `status`.

### Key sources

- Local: PEM file, `X509Certificate2`, or an in-memory `ECDsa`/`RSA`. `kid` defaults to the RFC 7638 JWK thumbprint. *(writer's call)*
- Azure Key Vault (`Ambinder.A2A.Azure.KeyVault`):
  - `AddAzureKeyVaultCardSigningKey(Uri keyId, TokenCredential? credential = null)`. Default credential (when `credential` is null) follows package 07's rule: in `Development`, `DefaultAzureCredential` (developer tools); in any other environment, `ChainedTokenCredential(WorkloadIdentityCredential, ManagedIdentityCredential)`. `DefaultAzureCredential` is not used in production because it probes many sources (slow, unpredictable). Package 08's push key source uses the same default. *(Reconciled with 07; was `DefaultAzureCredential` everywhere.)*
  - Signs with `CryptographyClient.SignAsync` on the SHA-256/384 digest, so the private key never leaves the vault.
  - `kid` is the Key Vault key version URL's last segment, prefixed by the key name (`name/version`). *(writer's call)*
  - Options (`AzureKeyVaultKeySourceOptions`), shared by card and push signing *(added for package 08's push key rotation)*:

    | Option | Default | Meaning |
    |---|---|---|
    | `RefreshInterval` | 1 h | How often the source re-reads the key versions. |
    | `PublishLead` | 1 h | A new version is published (JWKS) at once but used for signing only when its `CreatedOn` (or `NotBefore`) is at least this far in the past. So a rotated key is published before it signs. |
    | `RetireGrace` | 24 h | A version that is no longer the signing version stays in the published keys for this long after it was replaced or expired, so receivers with a cached JWKS still verify. |

  - Signing version: the newest enabled version that passed `PublishLead`. If none has passed it yet (first deployment), the newest enabled version is used. *(writer's call)*
  - Published keys: every enabled version, plus versions retired less than `RetireGrace` ago. Disabled versions are never used and never published. Expired versions are never used for signing. The card JWKS (`o.RetiredKeyGracePeriod`, above) and the push JWKS (08) are both built from this list.
  - If the first refresh fails, the source throws at first use. If a later refresh fails, the last known keys are kept and a warning is logged (08 adds its metric).
  - Needs Key Vault permission "sign" and "get" on keys; the docs section lists the exact RBAC role (Key Vault Crypto User).
- Package 08 implements push signing on top of `A2ASigningKey` and this Key Vault source (`AddAzureKeyVaultPushSigningKey`), so one Key Vault setup serves both.

### Test vectors

Folder `tests/vectors/card-signing/` (checked in, also used by package 16):
- `jcs/`: the RFC 8785 test inputs and outputs (numbers, strings, nesting).
- `spec-example/`: the card from spec 8.4 after PR #2319, with the expected canonical bytes.
- `presence/`: cards that test each field-table rule (REQUIRED at default, `optional` set to default, empty list, empty message, unknown field, Struct with defaults inside).
- `ours/`: cards we sign with fixed test keys (ES256 and RS256), with expected canonical bytes and the public JWKS.
- `python/`, `js/`: cards signed by the Python and JS reference SDKs at pinned versions, with their JWKS. Produced by package 16's harness and refreshed when those versions change. Each file notes which canonical form it matches (`spec`, `DropRequiredDefaults`, `Python112`).
- Each vector has a `README` line naming its source and the upstream issue if it shows a divergence.

## How it meets the six principles

1. Protocol: follows spec 8.4 text, and the spec example passes. Check: `spec-example` vector test.
2. .NET enabler: `AddCardSigning()` with options, `IHttpClientFactory` for JWKS, `TokenCredential` for Key Vault, `ILogger` and a `Meter`. Check: PublicAPI review in package 02.
3. Easy: signing in one line in development (`AddDevelopmentCardSigningKey()`), one more line for Key Vault; verify on by default with no config. Check: a sample with signing and verification under 10 extra lines; a test for each failure message.
4. Performance: sign once per card version; verify once per fetched card; JCS writes to an `IBufferWriter<byte>`. Check: package 14 benchmark for `GetSigningPayload` on a 20 KB card and for a full verify.
5. Modular: key source and verifier are interfaces; Azure lives in its own package. Check: `Ambinder.A2A.Protocol`, `Server` and `Client` have no Azure reference (a build test on package references).
6. Integration pains: canonicalization mismatch across SDKs is named and diagnosed, not just failed. Check: `python/` and `js/` vectors in CI with the expected status for each.

## Dependencies

Needs:
- 01: package names, including the Key Vault package.
- 03: card bytes caching and the ETag rule (hash of the unsigned canonical payload plus key ids, B13); `docs/spec-decisions.md`.
- 07: Azure.Identity setup patterns, so Key Vault uses the same credential story (the default credential rule above).
- 10: the card resolver calls `IAgentCardVerifier` on raw bytes and keeps the result with the card.
- 16: harness that produces Python and JS signed cards and runs their verifiers on ours.

Provides:
- `JsonCanonicalizer`, `AgentCardCanonicalizer` (to 16 and any package that needs JCS).
- `IAgentCardSigningKeySource`, `A2ASigningKey`, `JsonWebKeyPublic`, and the Key Vault source with `PublishLead` and `RetireGrace` (to 08 for push signing).
- `IAgentCardVerifier`, `CardVerificationOptions`, `CardVerificationResult`, `A2ACardVerificationException` (to 10).
- Test vectors in `tests/vectors/card-signing/` (to 16 and 12).

## Acceptance criteria

- [ ] `JsonCanonicalizer` passes every RFC 8785 test vector.
- [ ] Field table generated from `proto/a2a.proto`; a test fails if any card field lacks an entry.
- [ ] The spec 8.4 example canonicalizes to the expected bytes.
- [ ] Each `presence/` vector gives the expected payload.
- [ ] Server: signed public and extended cards; ES256 and RS256 tests; expired key skipped; two keys during rotation both sign; JWKS endpoint serves current and recently retired keys.
- [ ] Client: one test per `CardVerificationStatus` value and per policy; tampered card rejected; `jku` on another origin not trusted; JWKS size limit enforced.
- [ ] `CanonicalizationMismatch` is reported for a card signed with the "drop REQUIRED defaults" form when strict mode is on; with the default (`AcceptKnownCanonicalizationVariants = true`, C4) the card is `Verified` with a warning log.
- [ ] The ETag of a signed card is the same on two server instances and after re-signing with the same keys (B13).
- [ ] Interop (via package 16): Python and JS signed cards give the documented status; our signed card verifies in the Python and JS verifiers, or the gap is listed with its upstream issue.
- [ ] Key Vault key source: unit tests with a fake `CryptographyClient`, including `PublishLead` (a new version is published before it signs), `RetireGrace` (a replaced version stays published, then drops out) and the default credential per environment; a live test against a real vault that runs only when its secrets exist and is skipped visibly otherwise; until a vault exists, the manual pre-release checklist covers it (owner decision O3).
- [ ] A build check proves the core packages do not reference Azure packages.
- [ ] Docs section (package 13 format): "Sign your agent card" and "Verify agent cards", including Key Vault RBAC and the `IfPresent` behavior table.
- [ ] `docs/spec-decisions.md` row for A2A #2122 and for each wire-level writer's call here.

## Left to the builder

- The JCS number formatting algorithm (port of a known ECMAScript algorithm, or `double.ToString("R")` plus fixes), as long as RFC vectors pass.
- How the field table generator parses the proto.
- JWKS endpoint path default and route registration details.
- Log event ids and message wording, within principle 3.
- Whether variant canonicalizers live in the client package or the protocol package.

## Open questions

None. The default client policy (Q1) is Claude's call C4, in "Verification (client)".
