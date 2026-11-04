# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Versions come from git tags (`vX.Y.Z[-preview.N]`). Until 1.0.0, any preview can contain breaking changes.

<!--
Release pipeline: the validate job looks for a `## [X.Y.Z]` heading (with the pre-release part, for example
`## [1.0.0-preview.1]` for the tag `v1.0.0-preview.1`) for the tag being released, and the GitHub Release body is cut from that section.
Before tagging, rename `## [Unreleased]` content into a new `## [X.Y.Z]` section and keep an empty `## [Unreleased]` above it.
Every preview section keeps a `### Breaking changes` heading.
-->

## [Unreleased]

### Breaking changes

- **Everything is renamed from `A2A.*` to `Ambinder.A2A.*`.** This covers package IDs, assembly names, root namespaces and the
  solution (now `Ambinder.A2A.slnx`). The old IDs are not published. The new names avoid a clash with the official `A2A` package.
- **Telemetry names changed.** ActivitySource `A2A.Server` is now `Ambinder.A2A.Server`, ActivitySource `A2A.Client` is now
  `Ambinder.A2A.Client`, and the `A2A.Protocol` meter is now `Ambinder.A2A.Protocol`. Update `AddSource`, `AddMeter` and any
  dashboards or alerts that filter on these names.
- **Logger categories changed.** `A2A.Server.JsonRpc` and `A2A.Server.Rest` are now `Ambinder.A2A.Server.JsonRpc` and
  `Ambinder.A2A.Server.Rest`. Update log filters.
- **The server and client are split into seven packages:**
  - `Ambinder.A2A.Protocol`: models, JSON, constants, telemetry names.
  - `Ambinder.A2A.Server`: host-neutral server core (no ASP.NET Core, no gRPC).
  - `Ambinder.A2A.AspNetCore`: JSON-RPC and HTTP+JSON endpoints, `MapA2A`, `AddA2AServer`.
  - `Ambinder.A2A.AspNetCore.Grpc`: gRPC server service, `AddA2AGrpc`, `MapA2AGrpc`.
  - `Ambinder.A2A.Client`: client core with the JSON-RPC and HTTP+JSON transports.
  - `Ambinder.A2A.Client.Grpc`: gRPC client transport.
  - `Ambinder.A2A.Client.Entra`: Azure.Identity credential providers.
- **`AddA2AServer` moved** from the server package to `Ambinder.A2A.AspNetCore`. The host-neutral registration in
  `Ambinder.A2A.Server` is now `AddA2AServerCore`.
- **gRPC is opt-in.** On the server, reference `Ambinder.A2A.AspNetCore.Grpc` and call `AddA2AGrpc()` (and `MapA2AGrpc`). On
  the client, reference `Ambinder.A2A.Client.Grpc` and add `GrpcClientTransport.Instance` to `A2AClientOptions.Transports`.
  The core client builds only JSON-RPC and HTTP+JSON clients.
- **`AzureIdentityCredentialProvider` moved** to the `Ambinder.A2A.Client.Entra` package. `Ambinder.A2A.Client` no longer
  depends on `Azure.Identity`.
- **`A2A.Protocol.Grpc` is removed.** The generated protobuf types and `ProtoMapper` are internal in both gRPC packages, and
  the gRPC service class is internal.
- **`A2AJsonContext` and `UtcTimestampConverter` are now internal** (`Ambinder.A2A.Protocol`). Use the `A2AJson` accessors or `A2AJson.TypeInfoResolver` instead. `ProtoEnumConverter<T>` stays public, only because the public enums `TaskState` and `Role` name it in `[JsonConverter]` and the source generator needs an accessible converter; do not use it directly.
- **Server plumbing is now internal** (`Ambinder.A2A.Server`): `TaskManager`, `TaskEventBroker`, `TaskWriter.Releaser`, `PushNotificationSender`, `PushSigningKey`, `TracingRequestHandler`, `PageToken`, `TaskStateRules`, `WebhookUrlValidator`, `AgentCardInterfaces`, `InMemoryTaskStore` and `InMemoryPushConfigStore`. Use `IA2ARequestHandler`, `ITaskStore` and `IPushConfigStore`; `AddA2AServer` registers the defaults.
- **ASP.NET Core plumbing is now internal** (`Ambinder.A2A.AspNetCore`): `RequestValidation`, `ServiceParameters`, `RestErrorWriter`, `SseWriter` and `AgentAuthorizer` (use `IAgentAuthorizer`).
- **`GrpcErrorMapper` is now internal** (`Ambinder.A2A.AspNetCore.Grpc`).

### Added

- NuGet package metadata (description, tags, license expression `Apache-2.0`, README and icon in every package).
- SourceLink, deterministic builds and `.snupkg` symbol packages.
- Versions from git tags with MinVer.
- `LICENSE` (Apache-2.0) and `NOTICE`, included in every package.
- Trim and AOT analyzers (`IsAotCompatible`) on `Ambinder.A2A.Protocol`, `Ambinder.A2A.Server`, `Ambinder.A2A.AspNetCore`
  and `Ambinder.A2A.Client`.
- Community files: `CHANGELOG.md`, `SECURITY.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `.github/CODEOWNERS`, issue forms
  and a pull request template.
- `A2AJson.TypeInfoResolver` and one typed `JsonTypeInfo<T>` accessor per protocol type (for example `A2AJson.AgentTask`, `A2AJson.Message`), so an app with its own source-generated JSON context can add the A2A types.

<!-- No release tag exists yet. With the first tag, change this link to https://github.com/roman-ambinder/a2a-starter/compare/vX.Y.Z...HEAD and add a link per release. -->
[Unreleased]: https://github.com/roman-ambinder/a2a-starter/commits/master
