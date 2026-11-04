# Public API review checklist

Use this list in the API review before `1.0.0-rc.1` (spec 02, section 9). One pass by the builder over every package, driven by the API diff (`PublicAPI.Unshipped.txt` in each `src/Ambinder.A2A.*` project). The output is a short list of fixes. All fixes land before `rc.1`.

How to run it:

1. Build `Ambinder.A2A.slnx` in Release and run the tests. The API files, the XML doc lint and the surface guard tests must pass first.
2. Read every line of every `PublicAPI.Unshipped.txt`. Tick the boxes below for each package.
3. Fix or record each finding. A finding that needs an owner decision goes into the "Open items" section with the decision.

## 1. Review items (spec 02, section 9)

- [ ] **Names.** Types and members follow .NET naming. Prefix `A2A` only where a name needs it (`A2AException`, `IA2AClient`). Registration methods are `AddA2A...`, mapping methods are `MapA2A...`, in the namespaces the web SDK imports.
- [ ] **Sealing.** Every public class is `sealed`, unless inheritance is a designed extension point. A non-sealed class has a line in its XML docs about how to inherit and which members may be overridden. Default implementations are `sealed` and `internal` where possible.
- [ ] **Nullability.** Every `?` and `!` in the API files is intended. No public member returns `null` where an empty list would do.
- [ ] **Async shape.** Every async method ends in `Async`, takes a `CancellationToken` last, and returns `Task` or `ValueTask`.
- [ ] **Options.** Options classes are `sealed`, have a public parameterless constructor and settable properties, bind from `A2A:...` configuration, and fail validation at startup with a message that names the property, the bad value and the fix.
- [ ] **Namespaces.** One namespace per concern. No type lives in a namespace that contains `Internal`.
- [ ] **XML docs.** The `XmlDocQualityTests` lint passes. Read the summaries of the day-one types (`IAgentHandler`, `AddA2AServer`, `IA2AClient`) once, as a new user would.
- [ ] **Forbidden types.** The `PublicSurfaceTests` guard passes: no public signature mentions a type outside the package allow-list, and no type from the "must be internal" list is public.

## 2. Evolution rules (spec 02, section 4)

- [ ] **Implementer-facing interfaces** (`IAgentHandler`, `ITaskStore`, `IPushConfigStore`, `IOwnerResolver`, `IAgentAuthorizer`, `ITaskEventObserver`, `IA2ACredentialProvider`, `IAgentCardContributor`): after `1.0.0`, new behavior is added as a default interface method, never as a new abstract member.
- [ ] **Consumer-facing interfaces that users mock** (`IA2AClient`): same rule. Convenience calls are extension methods.
- [ ] **Models** are `sealed record` types with `init` properties and no positional constructor (see the open items for the exceptions).
- [ ] **New model properties** have a default that keeps old behavior and are not `required`.
- [ ] **Secret-holding records** override `PrintMembers`, and a test checks `ToString()` (`AuthenticationInfo`, `TaskPushNotificationConfig`).
- [ ] **Enums** say "More values can be added in later versions; handle unknown values." in their remarks (`TaskState`, `Role`, `A2AErrorCode`).
- [ ] **No new optional parameters** on an existing method after `1.0.0`. Add an overload.
- [ ] **Return types** are `IReadOnlyList<T>`, `IReadOnlyCollection<T>` or `IAsyncEnumerable<T>`, not `List<T>`.
- [ ] **Argument checks.** Public methods use `ArgumentNullException.ThrowIfNull` and friends. Protocol failures throw `A2AException` with an `A2AErrorCode`. Messages say what the caller must do.
- [ ] **Dependencies in the API.** A third-party type in a public signature is on the allow-list in spec 02, section 3.
- [ ] **Experimental and obsolete API** use `[Experimental("A2AEXP...")]` and `[Obsolete(..., DiagnosticId = "A2AOBS...")]`, each with a unique id.
- [ ] **No new static singletons** in public API (spec 02, "How it meets the six principles", item 6).

## 3. Open items found while building package 02

Each item needs a decision or a check in the review. Tick it when it is done.

### Public types that are public on purpose, for now

- [ ] `ProtoEnumConverter<T>` is public only because the public enums `TaskState` and `Role` name it in `[JsonConverter]`, and the System.Text.Json source generator needs an accessible converter in a consumer's own `JsonSerializerContext` (owner decision, 2026-11-04). Check that it is still needed.
- [ ] `IA2AClientTransport`, `A2AClientOptions.Transports` and `GrpcClientTransport` are the interim seam from package 01. Package 10 replaces them. Remove them from the API files when it lands.
- [ ] `A2AClientFactory` stays public until package 10 deletes it in favour of `A2AClient.Create` and `IA2AClientFactory`.
- [ ] The only public type in `Ambinder.A2A.Client.Grpc` is `GrpcClientTransport` (Task 4.3). Confirm nothing else is needed.
- [ ] Names that spec 03 leaves to package 02 and that the package 03 plan chose: `A2AVersionDispatchContext` and `IA2ACallerResolver` (they replace `A2AAgentRoute`; package 15 plan D1, owner decision 2026-11-03), and `A2AException.Details` next to `A2AException.Data`. Decide whether both `Data` and `Details` stay public. Today `IA2ACallerResolver`, `IA2AVersionDispatcher` and `A2AVersionDispatchContext` are `internal` in `Ambinder.A2A.AspNetCore`; check that this is still the intended state.
- [ ] Added by package 03 and now public: `IAgentCardContributor` and `AgentCardContext` (`Ambinder.A2A.Server.Hosting`), `A2AException.Details`, `A2AConstants.JsonRpcContentType`, `A2AServerOptions.InterfaceTenants`. `InterfaceTenants` goes away if package 07's `A2ASecurityOptions.Tenants` replaces it.
- [ ] The server signer seam from the package 04 plan (decision P18, owner decision 2026-11-03): `IAgentCardSigner` and `SignedAgentCard` in `Ambinder.A2A.Server.Signing`. Not present yet.

### Positional public records

Positional parameters are API: adding or reordering one is a break. These records are positional today. The owner packages convert them.

- [ ] `StoredTask` (converted by package 05).
- [ ] `A2ACallContext` (package 07, Task 3).
- [ ] `AgentRequestContext` (package 11, Task 1).
- [ ] `AgentRegistration` (package 11, Task 4).
- [ ] `A2AErrorInfo` (no plan yet; convert it in this review if no package has).
- [ ] The store key structs that spec 05 keeps positional (owner decision 2026-11-03): `TaskKey`, `OwnerScope`, `TaskCursor`, `TaskRef`, `CreateResult`, `OutboxStats`. They are the only positional public types allowed. Every other public record uses properties.

### Exceptions

- [ ] `A2AAuthException` is not sealed and does not derive from `A2AException`. Decide: seal it, and decide whether it should derive from `A2AException`.

### Analyzer rules turned down or suppressed

- [ ] **CA1716 is turned off** in `.editorconfig`: the public parameter `call` of `IA2ARequestHandler` clashes with the Visual Basic keyword `Call`. Decide whether to rename the parameter (a source break for callers who name it) or keep the exception.
- [ ] **RS0030 pragma** in `A2AStartupValidation`: `GetAwaiter().GetResult()` inside the synchronous `IStartupFilter`. Package 09 moves startup checks; remove it then.
- [ ] **RS0030 pragma** in `ClientSupport.CreateOwnedHttpClient`: `new HttpClient()` for a client the SDK owns. Package 10 replaces it with `IHttpClientFactory`.
- [ ] **CA2208 pragmas** in `ClientSupport.EffectiveTenant` (parameter name `request`) and in `GrpcA2AClient` (`name`, a header name). Both are deliberate; read them once.
- [ ] **`InternalServiceKeys`**: the keyed `Func<JsonObject>` service that serves the push JWKS between `Ambinder.A2A.Server` and `Ambinder.A2A.AspNetCore` is an internal seam. Package 08 replaces it.
- [ ] The RS0030 pragmas use `#pragma`, not `[SuppressMessage]`. If package 14 Task 21 counts suppressions with `[SuppressMessage]`, make sure it also finds these.

### Test access

- [ ] The `Ambinder.A2A.Integration.Tests` `InternalsVisibleTo` exception (owner decision 2026-11-03, Task 3.1) is on `Ambinder.A2A.Server`, `Ambinder.A2A.AspNetCore` and `Ambinder.A2A.Client`. Check that it is still needed once packages 10 and 12 have reworked those tests. No `InternalsVisibleTo` exists between shipped assemblies.

### Generated API files

- [ ] `PublicAPI.Shipped.txt` is empty in every project (`#nullable enable` only) until `rc.1`. At `rc.1` the lines of `Unshipped` move to `Shipped`, and `ApiFileTests.Shipped_files_are_empty_during_previews` is removed.
