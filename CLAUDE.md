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

HybridCLR AOT + Hotfix architecture:

**AOT Layer** (compiled by IL2CPP, not hot-reloadable):
- `Assets/Scripts/AOT/Core/Resource/` - ResourceManager, HandleEntry, Res
- `Assets/Scripts/AOT/KcpNet/` - KCP networking (Client/Server/Common)
- `Assets/Scripts/AOT/DataDefinition/` - GameConsts, GameSettings

**Hotfix Layer** (compiled to DLL, hot-reloadable):
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/` - 3C系统 (Character, Control, Camera)
- `Assets/Scripts/Hotfix/GameSystems/Skills/` - 技能系统
- `Assets/Scripts/Hotfix/GameSystems/Bag/` - 背包系统
- `Assets/Scripts/Hotfix/GameSystems/UI/` - UI框架
- `Assets/Scripts/Hotfix/GameSystems/Monster/` - 怪物系统
- `Assets/Scripts/Hotfix/GameSystems/Combat/` - 战斗系统 (Hitbox, AttackShape)
- `Assets/Scripts/Hotfix/GameSystems/Nameplate/` - 名字标签
- `Assets/Scripts/Hotfix/GameSystems/VFX/` - 武器特效

**Examples** (reference code):
- `Assets/Scripts/Examples/` - KcpNet usage examples (ClientExample, ServerExample)

---

## 3C System (Character, Control, Camera)

三层FSM架构，位于 `Assets/Scripts/Hotfix/GameSystems/Sys3C/`:

### Core Components

| Component | Path | Purpose |
|-----------|------|---------|
| `FSMManager` | `FSM/` | 协调 BaseFSM、AttackFSM、HitFSM |
| `StateCoordinator` | `Core/` | 层间协调，通过反射调用各FSM |
| `CharacterController` | `Character/` | 物理移动、重力、跳跃、伤害处理 |
| `AnimationDriver` | `Animation/` | Animator参数驱动，FSM与Animator桥梁 |
| `EventBus` | `Core/Events/` | 轻量级事件发布订阅系统 |
| `SkillDashComponent` | `Skill/` | 技能突进逻辑（碰撞检测、阶段控制） |
| `GroundDetector` | `Character/` | 地面检测 |
| `ThirdPersonCameraController` | `Camera/` | 第三人称相机控制 |

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

**AttackFSM** (`FSM/AttackState.cs`):
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

回调通过 `StateMachineBehaviour` (`Animation/StateBehaviours/`):
- `BaseStateBehaviour` - Base层动画完成回调
- `AttackStateBehaviour` - Attack层动画完成回调
- `HitStateBehaviour` - Hit层动画完成回调

### Network Sync

位于 `Sys3C/Network/`:
- `NetworkBridge` - 网络桥接
- `NetworkPrediction` - 客户端预测
- `PositionInterpolator` - 位置插值
- `SkillNetworkSync` - 技能网络同步
- `MovementPolicy` - 移动策略

### Adapters (适配器模式)

| Adapter | Purpose |
|---------|---------|
| `CharacterStatsAdapter` | 角色属性（Health, Attack, Defense...） |
| `ShieldSystemAdapter` | 护盾系统（伤害吸收） |
| `PhysicsSystemAdapter` | 物理系统 |
| `StatusControllerAdapter` | 状态控制器（眩晕等） |

---

## Skill System

位于 `Assets/Scripts/Hotfix/GameSystems/Skills/`:

**数据层** (`Definition/` / `Data/`):
- `SkillData` - 技能基础数据
- `SkillState` / `SkillType` / `SkillSubState` - 状态和类型定义
- `EffectBlock` / `ShapeBlock` / `DamageBlock` - 技能效果/形状/伤害数据块
- `PresentationBlock` - 表现层数据
- `InstantSkillData` / `ProjectileSkillData` / `ComboSkillData` / `ChargedSkillData` / `ChanneledSkillData` - 技能类型

**运行时** (`Runtime/`):
- `SkillStateMachine` - 技能状态机
- `SkillExecutor` - 技能执行器（生命周期管理、目标检测、伤害应用）
- `SkillCoordinator` - 技能协调器（输入处理、状态同步）
- `SkillInputBuffer` - 技能输入缓冲
- `CooldownManager` - 冷却管理
- `SkillInterruptionMatrix` - 技能打断矩阵

**特效** (`Effect/` / `Events/`):
- `EventBus` - 技能事件总线
- `SkillVFXEvents` - VFX事件
- `DamageEnums` - 伤害枚举

---

## Combat System

位于 `Assets/Scripts/Hotfix/GameSystems/Combat/`:
- `AttackHitbox` / `PlayerHitZone` - 攻击检测器
- `HitZone` - 受击区域

攻击形状系统 (`Sys3C/Core/Combat/`):
- `IAttackShape` - 攻击形状接口
- `CircleShape` / `RectShape` / `ConeShape` / `SectorShape` - 攻击形状实现
- `AttackShapeFactory` / `AttackShapeGizmos` - 工厂和调试

伤害系统 (`Sys3C/Core/Combat/`):
- `IDamageable` / `ITargetable` / `IWeapon` - 伤害接口
- `IAttackHitbox` / `IHitDetector` / `IEntityRegistry` - 检测接口
- `PhysicsRegistry` - 实体注册表
- `AttackHitboxData` / `WeaponConfig` - 配置数据

---

## Monster System

位于 `Assets/Scripts/Hotfix/GameSystems/Monster/`:
- `MonsterAI` - 怪物AI主控制器（状态机：Idle/Patrol/Chase/Attack/Hit/Death/Defend/Taunt/Alert）
- `MonsterEntity` / `MonsterStats` - 怪物实体和属性
- `MonsterSpawner` / `MonsterConfig` - 生成器和配置
- `IAIBehaviour` + 4个Behaviour实现 (Alert, Defend, Taunt, Movement) - AI行为接口和实现
- `MonsterLootTable` - 掉落表
- `MonsterEvents` - 事件定义

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
- `UIAnimation` / `SequenceBuilder` / `UIAnimPreset` - 动画系统
- `UITweenExtensions` - 补间动画扩展
- `ScreenAdapter` - 屏幕适配
- Components: Loading, Toast, Tips, Confirm
- Panels: HUD/TargetPanel

---

## Nameplate System

位于 `Assets/Scripts/Hotfix/GameSystems/Nameplate/`:
- `EntityDisplayManager` - 实体显示管理
- `NameplateRenderer` / `FloatTextRenderer` - 名字标签和浮动文字渲染
- `NameplateSettings` / `FloatTextSettings` - 显示设置
- `DisplayEventBridge` - 显示事件桥接
- `DamageScreenEffect` - 伤害屏幕效果

---

## VFX System

位于 `Assets/Scripts/Hotfix/GameSystems/VFX/`:
- `WeaponVFXController` - 武器特效控制器
- `WeaponTrailRenderer` / `WeaponMistParticles` - 武器拖尾和粒子
- `SlashTrailVFX` / `SwordGlowVFX` / `FrostAuraVFX` / `IceBurstVFX` / `IceDecalVFX` - 技能特效
- `HitFlashVFX` - 受击闪烁
- `WeaponElementConfig` / `WeaponMaterialProxy` / `WeaponSurfaceShader` - 武器材质配置
- `SkillFreezeEffector` - 冰冻效果器

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
- [x] Skill System (技能Q/R + 突进组件 + 数据层)
- [x] Hit System (受击/击退/倒地/死亡)
- [x] UI Framework
- [x] Bag System
- [x] Monster System (AI + Behaviour + Spawner)
- [x] Combat System (Hitbox + AttackShape)
- [x] Camera System (ThirdPersonCameraController)
- [x] VFX System (Weapon + Skill effects)
- [x] Nameplate System (Entity display)
- [ ] NPC Mirror system