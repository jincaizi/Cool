# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Unity MMO Client Project

## Project Overview
Unity 2022 LTS MMO client with server integration planned. Code assisted by Claude Code.

## Tech Stack
- Unity: 2022.3.25f1 (LTS)
- Networking: KCP + Protobuf
- Resource Management: Addressable
- Hotfix: HybridCLR (code hot-reload)
- UI: UGUI-based custom framework

---

## Common Commands

### Unity Editor
- Open project in Unity Editor: `start Unity.exe -projectPath E:\CodeForJob\Cool`
- Build player: Use Unity Build window (Ctrl+Shift+B)

### Git
- Commit changes: Use `git add <files>` then `git commit -m "<message>"`
- LFS tracking: Files are tracked with Git LFS

---

## Architecture

### Code Organization

The project maybe uses HybridCLR's AOT + Hotfix architecture in future:

**AOT Layer** (compiled by IL2CPP, not hot-reloadable):
- `Assets/Scripts/AOT/Core/` - Event, Resource, Input, ObjectPool
- `Assets/Scripts/AOT/KcpNet/` - KCP networking (Client/Server/Common)
- `Assets/Scripts/AOT/DataDefinition/` - Interfaces, Enums, Event constants

**Hotfix Layer** (compiled to DLL, hot-reloadable):
- `Assets/Scripts/Hotfix/GameSystems/` - Game logic (3C, Bag, etc.)
- `Assets/Scripts/Hotfix/Entry/` - Hotfix entry point

**Examples** (reference code):
- `Assets/Scripts/Examples/` - KcpNet usage examples (ClientExample, ServerExample)

### Key Implementation Details

**KCP Networking** (`Assets/Scripts/AOT/KcpNet/`):
- `KcpClient` / `KcpServer` - High-level session management
- `KcpClientTransport` / `KcpServerTransport` - Transport layer
- `IKcpTransport` - Transport interface
- Built-in messages: LoginRequest/Response, ChatMessage, Heartbeat, Kick, PositionSyncRequest
- Uses `IMessageExecutor` to execute callbacks; `UnityMainThreadExecutor` for Unity main thread dispatch
- Reliable vs Unreliable message flags via `MessageFlags`

---

## Development Standards
- Interface-oriented programming, high cohesion and low coupling
- Dependencies: Follow layered architecture; 
- Resources: Addressable async loading, object pooling for frequent objects
- Version: Git + Git LFS

---

## Current Status
- [x] KCP + protobuf networking (Assets/Scripts/AOT/KcpNet/)
- [ ] Module framework (AOT/hotfix structure)
- [ ] 3C system (placeholder: Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3C-pre.md contains spec)

