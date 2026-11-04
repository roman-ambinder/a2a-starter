# Versioning policy

This page says what each version number promises and what counts as public API. It follows [Semantic Versioning 2.0](https://semver.org/spec/v2.0.0.html).
The public surface of every package is tracked in its `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`; see [CONTRIBUTING.md](../../CONTRIBUTING.md#public-api) for the workflow.

## 1. Version numbers

Format `MAJOR.MINOR.PATCH`. All packages share one version.

- **Patch:** bug fixes, security fixes, doc fixes, performance fixes. No API change. Behavior changes only to match the A2A spec or the docs.
- **Minor:** new API, new packages, new options with safe defaults, new experimental API, additive spec features (for example A2A 1.1 features), deprecations.
- **Major:** removal of obsolete API, any break to a stable API, a raise of the minimum .NET version, a new A2A major spec version as the default.
- **Previews (`-preview.N`):** anything may change. Breaks are listed under "Breaking changes" in [CHANGELOG.md](../../CHANGELOG.md). Preview packages are for early adopters, not for production dependencies of libraries.
- **Release candidates (`-rc.N`):** the API is frozen. Only fixes.

### What is breaking

- Removing or renaming a public member.
- Changing a signature, a return type, a nullability annotation to a stricter one, or a default that changes behavior.
- Adding a member to an implementer-facing or mocked interface without a default implementation.
- Adding an abstract member to a public class.
- Changing a JSON name, header name, telemetry name, configuration key, log EventId or error code.
- Changing a service lifetime.
- Raising a dependency's major version when its types are in our API.

### What is not breaking

- A new class or method.
- A new property on a model.
- A new enum member.
- A new option with a default.
- A new optional wire field.
- Anything `internal`.

## 2. What counts as API

Public API is anything a consumer can compile against or depend on at run time:

1. Public and protected types and members in the shipped assemblies (tracked by the API files).
2. **Wire contract:** JSON names and shapes, enum strings, header names, `ErrorInfo` reasons, REST paths, JSON-RPC method names. Changed only by a spec version change.
3. **Configuration:** option class names and property names, config section names (`A2A:...`) and their defaults. Changing a default that affects security or data is breaking and is called out in the changelog, even in a minor release if it is a security fix.
4. **Telemetry:** `ActivitySource` and `Meter` names, span names, metric names and tag names.
5. **DI behavior:** which services an `Add...` method registers, their lifetimes, and which are registered with `TryAdd` (replaceable). Documented in the XML docs of the `Add...` method.
6. **Exceptions and codes:** the exception types a method throws and the A2A error codes. Message text is not API.
7. **Log event IDs:** the `EventId` of each log message (section 8). Message text is not API.

Not API: anything `internal`, the namespace of an internal type, private members, the exact text of messages, the order of log lines, the timing of events beyond what the XML docs promise.

Two public types exist only because a tool needs them and are not meant for direct use; they are still tracked in the API files:
`ProtoEnumConverter<T>` (the public enums `TaskState` and `Role` name it in `[JsonConverter]`, and the JSON source generator needs an accessible converter; owner decision 2026-11-04),
and, until package 10 lands, the client transport seam (`IA2AClientTransport`, `A2AClientOptions.Transports`, `GrpcClientTransport`) and `A2AClientFactory`.

## 3. Evolution rules

**Interfaces.** *Implementer-facing* interfaces (users write classes that implement them, such as `IAgentHandler`, `ITaskStore`, `IPushConfigStore`, `IOwnerResolver`, `IA2ACredentialProvider`) and *consumer-facing* interfaces that users mock (`IA2AClient`) do not get new abstract members after `1.0.0`.
Add new behavior as a default interface method, or as a new interface that a feature checks for at run time. Convenience calls on `IA2AClient` are extension methods.

**Classes.** `sealed` unless inheritance is a designed extension point. A non-sealed public class has a line in its XML docs saying how to inherit and which members may be overridden. `A2AException` is the only intended base class today.

**Records and models.** Public models are `sealed record` types with `init` properties and no positional constructor. Adding a property is a minor change; it needs a default that keeps old behavior and must not be `required`.
A record that can hold a secret overrides `PrintMembers` so `ToString()` and logs never show the secret.

**Enums.** Adding a member is allowed in a minor release. Handle unknown values; do not rely on numeric values.

**Methods.** Async methods end in `Async` and take a `CancellationToken` last. After `1.0.0` there are no new optional parameters on an existing method: add an overload. Return `IReadOnlyList<T>`, `IReadOnlyCollection<T>` or `IAsyncEnumerable<T>`, not `List<T>`.

**Options.** Options classes are `sealed`, have a public parameterless constructor and settable properties, bind from `A2A:...` configuration, and are validated at startup. A failing validation names the property, the bad value and the fix. New options get safe defaults. Removing an option or changing its meaning is breaking.

**Registration.** `AddA2AServerCore` (in `Ambinder.A2A.Server`, host-neutral) and `AddA2AServer` (in `Ambinder.A2A.AspNetCore`, which adds the HTTP endpoints) are two separate methods by design (decision A14, 2026-11-03). Both live in `Microsoft.Extensions.DependencyInjection`.

**Dependencies in the API.** A third-party type in a public signature makes that library part of our contract. Only the allow-list enforced by `PublicSurfaceTests` may do that.

**`InternalsVisibleTo`.** Only to the matching `*.Tests` assembly, `Ambinder.A2A.Benchmarks`, and `Ambinder.A2A.Integration.Tests` (owner decision 2026-11-03), none of which is shipped. Never between shipped assemblies.

## 4. Experimental API

New or unsettled API after `1.0.0` carries `[Experimental("A2AEXP001")]` (the BCL attribute) and a line in its XML docs. Experimental API may change in any release.
Each experimental feature has its own id, listed here. An experimental API leaves that state in a minor release, with a changelog note.

| Id | Feature | Since | Planned exit |
|---|---|---|---|

No experimental API exists yet. The next free id is `A2AEXP001`.

## 5. Deprecation

An obsolete API is marked `[Obsolete("Use X instead.", DiagnosticId = "A2AOBS001")]`, with a unique id per API and a link in the message.
It stays at least one full minor release with a warning, and is removed only in the next major release. A security-related change may shorten this, with a changelog note.

| Id | Obsolete API | Replacement | Since | Removal |
|---|---|---|---|---|

The table has no rows yet. `ImplicitOAuthFlow`, `OAuthFlows.Implicit`, `PasswordOAuthFlow` and `OAuthFlows.Password` are already `[Obsolete]` but carry no diagnostic id; package 03 assigns `A2AOBS001` and `A2AOBS002` and adds the rows (replacement: none, they are protocol fields kept while the A2A spec has them).

## 6. Relation to the A2A spec

SDK 1.x implements A2A 1.x. Additive spec changes arrive in a minor SDK release. A2A 2.0 would be SDK 2.0.
Choices where the spec is open are written in [spec-decisions.md](../spec-decisions.md). Changing one counts as a minor change with a changelog note, and as a break only if a stable behavior is removed.

## 7. Support

The latest minor of the latest major gets fixes. The previous minor gets security fixes for three months after the next minor ships. The target framework is `net10.0` only.

## 8. Log EventId blocks

Log messages use `[LoggerMessage]` with a fixed `EventId`. An EventId never changes and is never reused. Each assembly owns one block of 1000 ids;
`LogEventIdTests` fails when an id is outside its assembly's block or used twice.

| Assembly | Block | Used so far |
|---|---|---|
| `Ambinder.A2A.Protocol` | none | No logging. |
| `Ambinder.A2A.Server` | 1000 to 1999 | 1001 to 1099 task area, 1101 to 1199 push delivery. |
| `Ambinder.A2A.AspNetCore` | 2000 to 2999 | 2001 unhandled transport error. |
| `Ambinder.A2A.AspNetCore.Grpc` | 3000 to 3999 | 3001 unhandled transport error. |
| `Ambinder.A2A.Client` | 4000 to 4999 | Reserved. The package does not log yet. |
| `Ambinder.A2A.Client.Grpc` | 5000 to 5999 | Reserved. The package does not log yet. |
| `Ambinder.A2A.Client.Entra` | 6000 to 6999 | Reserved. The package does not log yet. |

## 9. Timeline to 1.0

| Point | State |
|---|---|
| Now to first preview | Rename, hide internals, generate the first API files, turn on XML docs and analyzers. |
| `1.0.0-preview.N` | Each package adds its API to its `PublicAPI.Unshipped.txt` as it lands. Nothing moves to `Shipped`. Breaks go to the changelog. |
| API review (before `rc.1`) | One review pass over every package using the [API review checklist](api-review-checklist.md). All fixes land before `rc.1`. |
| `1.0.0-rc.1` | `Unshipped` is merged into `Shipped`. Only fixes after this. |
| `1.0.0` | `PackageValidationBaselineVersion=1.0.0`. This page is published. |
