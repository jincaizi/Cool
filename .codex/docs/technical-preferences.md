<!-- generated, do not edit; source: docs/technical-preferences.md; source-sha256: 05761703d9d3a2715c41bddfc6187eb1042d253e2d0b858b579d9290fb2ad8b9 -->
# Technical Preferences

## Engine & Language

- Engine: Unity 2022.3.25f1 LTS
- Language: C# (Unity .NET Standard 2.1 profile); server .NET 8
- Rendering: Built-in Render Pipeline (no URP package in `Packages/manifest.json`)
- Physics: Unity PhysX (client) + server navmesh/ORCA

## Input & Platform

- Target Platforms: Android, iOS, WebGL, Windows (Editor/Standalone tests)
- Input Methods: Touch, Keyboard/Mouse
- Primary Input: Touch (mobile), Keyboard/Mouse (Editor/Standalone)
- Gamepad Support: None
- Touch Support: Full
- Platform Notes: Android/iOS use KCP + Protobuf with the ADR-0003 security provider in public candidates; WebGL uses WebSocket/WSS; one primary transport per platform.

## Naming Conventions

- Classes: PascalCase
- Variables: camelCase; private fields `_camelCase`
- Signals/Events: PascalCase event names; handlers use `On`/`Handle` prefixes as in existing code
- Files: PascalCase `.cs`; asmdef names dotted, e.g. `GameSys.EditorQuality`
- Scenes/Prefabs: PascalCase
- Constants: PascalCase in `*Consts` classes

## Performance Budgets

- Target Framerate: 30 FPS on the Android reference device
- Frame Budget: p95 <= 33.3 ms
- Draw Calls: measure per scene; no fixed budget until the Android reference profile is filled
- Memory Ceiling: no fixed budget until the Android reference profile is filled; steady state must avoid per-frame GC

## Testing

- Framework: NUnit (Unity EditMode/PlayMode), xUnit (server)
- Minimum Coverage: trend baseline; no target assembly may drop more than 1 percentage point
- Required Tests: protocol roundtrip, movement validation, login, bag integrity, resource lifecycle, VFX/monster pool lifecycle

## Forbidden Patterns

- Non-internal MonoBehaviour `GetComponent`/`FindObject*`
- Gameplay `Resources.Load*`
- `AsyncOperationHandle.Task` and `WaitForCompletion()` in player paths
- `async void` connection flows
- Unbounded queues or silent drops of reliable messages
- Client-authoritative movement, defend, or combat state
- Plaintext KCP in non-Development public candidates
- `.Codex/` path variants (only lowercase `.codex/` is valid)

## Allowed Libraries / Addons

- UniTask, Addressables, protobuf-net, KCP (mobile only with ADR-0003 security provider), DOTween, TextMeshPro, NUnit, xUnit
- Security provider: selected by ADR-0003 after POC; no self-rolled cryptographic primitives

## Architecture Decisions Log

- ADR-0001: platform transport matrix
- ADR-0002: quality gate evidence model
- ADR-0003: KCP session security
