# ADR-0001: Platform Transport Matrix

## Status

Accepted — 2026-11-07.

## Context

The original design treated WebSocket/WSS as the default for all platforms and KCP as an experimental transport. The approved execution design changes this: Android/iOS use KCP + Protobuf as the primary transport, WebGL uses WebSocket/WSS, and Editor/Windows test lanes must select a transport explicitly. `ClientNetworkHost` currently constructs WebSocket directly on PC and WebGL, so production and test paths are not yet aligned.

## Decision

- Android/iOS Player builds use `KcpNetClientAdapter` + Protobuf; public candidates must add the ADR-0003 security provider.
- WebGL Player builds use `WebSocketClientAdapter` with the browser channel; public deployments use `wss://` through a reverse proxy.
- Editor/Windows tests select a transport through the test lane or explicit development configuration; there is no implicit default.
- Exactly one primary transport may be enabled per platform/build configuration.
- Only `NetworkTransportFactory` (or the approved equivalent) may construct concrete transports; the rest of `GameSys` depends on `INetworkClient` and immutable `NetworkEndpoint`.
- KCP plugin/adapter code must be excluded from WebGL builds, and `KcpPluginPlatformGuardTests` / `TransportFactoryBoundaryGuardTests` enforce the boundary.
- Task 13 owns the P0 implementation of the factory, endpoint, plugin exclusion, and guards. Task 30 only maintains the already-passing guards and allowlist.

## Consequences

- P0 must harden the KCP lifecycle and factory boundary before the P0 Gate; Android/iOS real-device security remains a Mobile Release Readiness item.
- WebGL must keep a separate compile/build lane and may not reference KCP types or plugins.
- The original Spec §6.0 WSS-default text must be rewritten; ADR-0001 is the authoritative record of the change.

## Alternatives

- All platforms use WSS: rejected because the approved platform matrix selects KCP for mobile.
- Mobile KCP without a security provider: rejected; public candidates require confidentiality and peer authentication.
- Per-platform asmdef copies of `GameSys`: rejected as a maintenance burden; compile guards plus factory boundary are sufficient.
