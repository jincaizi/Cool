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

HybridCLR's AOT + Hotfix architecture:

**AOT Layer** (compiled by IL2CPP, not hot-reloadable):
- `Assets/Scripts/AOT/Core/` - Event, Resource, Input, ObjectPool
- `Assets/Scripts/AOT/KcpNet/` - KCP networking (Client/Server/Common)
- `Assets/Scripts/AOT/DataDefinition/` - Interfaces, Enums, Event constants

**Hotfix Layer** (compiled to DLL, hot-reloadable):
- `Assets/Scripts/Hotfix/GameSystems/` - Game logic (3C, Bag, Skills, UI)
- `Assets/Scripts/Hotfix/Entry/` - Hotfix entry point

**Examples** (reference code):
- `Assets/Scripts/Examples/` - KcpNet usage examples (ClientExample, ServerExample)

---

## 3C System (Character, Control, Camera)

已完成的三层FSM架构，位于 `Assets/Scripts/Hotfix/GameSystems/Sys3C/`:

### Core Components

| Component | Path | Purpose |
|-----------|------|---------|
| `FSMManager` | `FSM/` | 协调 BaseFSM、AttackFSM、HitFSM |
| `StateCoordinator` | `Core/` | 层间协调，通过反射调用各FSM |
| `CharacterController` | `Character/` | 物理移动、重力、跳跃、伤害处理 |
| `AnimationDriver` | `Animation/` | Animator参数驱动，FSM与Animator桥梁 |
| `EventBus` | `Core/` | 轻量级事件发布订阅系统 |
| `SkillDashComponent` | `Skill/` | 技能突进逻辑（碰撞检测、阶段控制） |
| `GroundDetector` | `Character/` | 地面检测 |

### Three-Layer FSM

```
┌─────────────────────────────────────────────────────────────┐
│                      FSMManager                             │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐         │
│  │   BaseFSM   │  │  AttackFSM  │  │   HitFSM    │         │
│  └─────────────┘  └─────────────┘  └─────────────┘         │
└─────────────────────────────────────────────────────────────┘
        │                │                │
   LayerType.Base   LayerType.Attack  LayerType.Hit
        │                │                │
   Idle/Move/Sprint  Attack1/2/SkillQ  Hit/Knockback/
   JumpStart/Air/End  /SkillR          Launched/Dizzy/Down
```

**BaseFSM** (`FSM/BaseFSM.cs`):
- States: Idle, Move, Sprint, JumpStart, JumpAir, JumpEnd, Death
- 攻击状态下限制Base层状态转换，只允许跳跃相关

**AttackFSM** (`FSM/AttackFSM.cs`):
- States: Idle, Attack1, Attack2, SkillQ, SkillR_Start, SkillR_Loop
- 普攻连击系统、霸体机制、技能超时保护
- 集成 SkillDashComponent 实现突进

**HitFSM** (`FSM/HitFSM.cs`):
- States: None, Hit, Knockback, Launched, Dizzy, Down, GetUp, Death
- 优先级系统（Death > Down > Launched > ...）
- 击退/浮空物理模拟

### Animation System

Animator三层Layer架构：
- **Base Layer (index 0)**: Locomotion Blend Tree (Idle/Walk/Sprint)
- **Attack Layer (index 1)**: 普通攻击连击、技能Q/R动画
- **Hit Layer (index 2)**: 受击/击退/倒地/死亡动画

回调通过 `StateMachineBehaviour` (`Animation/StateBehaviours/`)：
- `BaseStateBehaviour` - Base层动画完成回调
- `AttackStateBehaviour` - Attack层动画完成回调
- `HitStateBehaviour` - Hit层动画完成回调

### Skill System

位于 `Assets/Scripts/Hotfix/GameSystems/Skills/` 和 `Sys3C/Skill/`:
- `SkillState` - 技能状态定义
- `SkillExecutor` - 技能执行器
- `SkillCoordinator` - 技能协调器
- `SkillInputBuffer` - 技能输入缓冲
- `SkillInterruptionMatrix` - 技能打断矩阵

### Network Sync

位于 `Sys3C/Network/`:
- `NetworkBridge` - 网络桥接
- `NetworkPrediction` - 客户端预测
- `PositionInterpolator` - 位置插值
- `SkillNetworkSync` - 技能网络同步

### Adapters (适配器模式)

| Adapter | Purpose |
|---------|---------|
| `CharacterStatsAdapter` | 角色属性（Health, Attack, Defense...） |
| `ShieldSystemAdapter` | 护盾系统（伤害吸收） |
| `PhysicsSystemAdapter` | 物理系统 |
| `StatusControllerAdapter` | 状态控制器（眩晕等） |

---

## KCP Networking

`Assets/Scripts/AOT/KcpNet/`:
- `KcpClient` / `KcpServer` - 高层会话管理
- `KcpClientTransport` / `KcpServerTransport` - 传输层
- `IKcpTransport` - 传输接口
- 内置消息: LoginRequest/Response, ChatMessage, Heartbeat, Kick, PositionSyncRequest
- 使用 `IMessageExecutor` 执行回调; `UnityMainThreadExecutor` 用于Unity主线程调度

---

## UI Framework

位于 `Assets/Scripts/Hotfix/GameSystems/UI/`:
- `UIManager` - UI管理器
- `UIPanel` - 面板基类
- `UIDataBinding` - 数据绑定
- `ViewModelBase` - ViewModel基类
- Components: Loading, Toast, Tips, Confirm

---

## Development Standards

- Interface-oriented programming, high cohesion and low coupling
- 适配器模式解耦外部系统依赖
- EventBus用于层间通信
- StateCoordinator通过反射调用各FSM方法
- 资源: Addressable异步加载，对象池管理高频对象
- 版本: Git + Git LFS

---

## C# Syntax Rules

- **禁止**在 MonoBehaviour 非内部类上使用 GetComponent
- 简单字符串插值用 `+`，避免复杂表达式在 Debug.Log 中

---

## Current Status

- [x] KCP + protobuf networking
- [x] 3C System (三层FSM + CharacterController + AnimationDriver)
- [x] Skill System (技能Q/R + 突进组件)
- [x] Hit System (受击/击退/倒地/死亡)
- [x] UI Framework
- [x] Bag System
- [ ] Module framework (AOT/hotfix structure)
- [ ] Camera system (placeholder in Sys3C/)
- [ ] NPC Mirror system

