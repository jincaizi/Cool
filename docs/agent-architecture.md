# Agent Architecture Reference

> Extracted from `CLAUDE.md` on 2026-10-21 to keep the always-loaded file small.
> Read this file when you need full system details; `CLAUDE.md` has the compact index and guardrails.

## Architecture

### Code Organization

AOT + Hotfix 程序集划分（**无 HybridCLR 热更**，见技术栈）:

**AOT Layer** (compiled by IL2CPP, not hot-reloadable):
- `Assets/Scripts/AOT/Core/Resource/` - ResourceManager, HandleEntry, Res
- `Assets/Scripts/AOT/KcpNet/` - KCP networking (Client/Server/Common)
- `Assets/Scripts/AOT/DataDefinition/` - GameConsts, GameSettings

**Hotfix Layer** (普通 asmdef 程序集，编译为 DLL，**不具热更能力**):
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

三层状态架构（Base / Attack / Hit），位于 `Assets/Scripts/Hotfix/GameSystems/Sys3C/`。
**只有两层有 FSM 类**（BaseFSM、HitFSM）——攻击层没有 `AttackFSM`，由 `StateCoordinator` + `SkillCoordinator` 驱动。

### Core Components

| Component | Path | Purpose |
|-----------|------|---------|
| `FSMManager` | `FSM/` | 协调 BaseFSM、HitFSM（**无 AttackFSM**） |
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
│                       FSMManager                            │
│  ┌─────────────┐                    ┌─────────────┐         │
│  │   BaseFSM   │                    │   HitFSM    │         │
│  └─────────────┘                    └─────────────┘         │
└─────────────────────────────────────────────────────────────┘
        │                │                │
   LayerType.Base   LayerType.Attack  LayerType.Hit
        │                │                │
   Idle/Move/Sprint  Attack1/2/SkillQ  Hit/Knockback/
   JumpStart/Air/End  /SkillR          Launched/Dizzy/Down
```

中间那格**没有 FSM 类**：攻击层由 `StateCoordinator` + `SkillCoordinator` 驱动
（`FSM/AttackState.cs` 只剩 2 值枚举 `Idle`/`Attacking`，仅用于驱动 Animator 整型参数）。

**BaseFSM** (`FSM/BaseFSM.cs`):
- States: Idle, Move, Sprint, JumpStart, JumpAir, JumpEnd, Death
- 攻击状态下限制Base层状态转换，只允许跳跃相关

**攻击层**（**无 `AttackFSM` 类**）：
- `FSM/AttackState.cs` 只剩 2 值枚举 `Idle` / `Attacking`，仅作为 Animator 整型参数（`AnimHashes.AttackState`）
- 层切换走 `StateCoordinator.SetAttackLayerActive()`，由 `SkillCoordinator` 经 `Sys3CEntry` 在技能激活时调用
- 普攻连击、霸体、技能超时保护与 `SkillDashComponent` 突进均在技能系统内实现，不在 FSM 里

**HitFSM** (`FSM/HitFSM.cs`):
- States: None, Hit, Knockback, Launched, Dizzy, Down, GetUp, Death
- 优先级系统（Death > Down > Launched > ...）
- 击退/浮空物理模拟

### Animation System

Animator三层Layer架构：
- **Base Layer (index 0)**: Locomotion Blend Tree (Idle/Walk/Sprint)
- **Attack Layer (index 1)**: 普通攻击连击、技能Q/R动画
- **Hit Layer (index 2)**: 受击/防御受击/眩晕动画（`Hit`/`DefendHit`/`Dizzy` 三态可达；击退复用 `Hit` 剪辑；**无 Knockback/Launched 状态，Death/Down/GetUp 无入边转移**——倒地/死亡/浮空的表现层未做）

回调通过 `StateMachineBehaviour` (`Animation/StateBehaviours/`):
- `BaseStateBehaviour` - Base层动画完成回调
- `AttackStateBehaviour` - Attack层动画完成回调
- `HitStateBehaviour` - Hit层动画完成回调

### Network Sync

位于 `Sys3C/Network/`:
- 网络同步草稿桩已于 P0 清理（NetworkBridge/NetworkPrediction/PositionInterpolator/SkillNetworkSync），共享协议层见 `Assets/Scripts/AOT/Protocol/`，路线图见 `docs/superpowers/specs/2026-08-27-network-sync-design.md`
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

## Movement, Collision & Avoidance

**碰撞的两半分开放，别只改一半**：

| 关注点 | 落在哪 | 关键约束 |
|---|---|---|
| 怪物 / 机器人**彼此**避让 | **服务端** `Server/Movement/`：`OrcaSolver.cs`（2D ORCA 速度解算，纯算法，有论文对照注释）+ `AvoidanceSystem.cs`（tick 快照、邻居筛选、接线） | **无邻居时解算结果逐字等于期望速度** —— 这条护栏保证既有布局/e2e 断言不受影响；动它必须同步改那两条"逐位相同"的用例 |
| **玩家 vs 怪物**阻挡 | **客户端** `SyncLayer/NpcBodyCollider.cs`：运行时按模型 bounds 生成，挂在**怪物视图**上 | 预制体本身**不挂**碰撞体（有资产断言）；`RemoteEntityAnimator` 对真人/bot 视图仍禁用碰撞体 —— 两者语义不同，别互相套用 |

**ORCA 避让**（`Server/Movement/`）：
- 参与者：**怪物 + 机器人**；**玩家不参与**（服务端改不了客户端权威的速度）⇒ 对智能体而言玩家是**零速度障碍**、责任权重 **1.0**（不信上报速度：朝向 ≠ 移动方向，且那是可控输入）；智能体彼此 **0.5**
- **不避让当前攻击目标**（`MonsterState.TargetId`）—— 否则追击的怪会绕着玩家转、永远进不了 `attackRange`
- 确定性：每 tick 开始按 id 排序拍快照（Jacobi）、逐个解算、统一应用 ⇒ 与遍历顺序无关
- 半径真值源 = `monster_config.json` 的 `bodyRadius`（装配时归一），客户端有断言"从预制体量出的半宽 ≈ 配置值"
- 调试开关：`--no-avoid`（负对照）、`--probe`（采样最小两两间距）
- **明确没做**：空间哈希、静态障碍线（世界边界仍由 `BotRunner` 处理）、预测性避让

**`NpcBody` 层（索引 10）**：怪物碰撞体专用；碰撞矩阵只放行本地玩家层（9）；**相机防穿墙掩码也必须排除它**（`ThirdPersonCameraController`，否则贴 Boss 时相机被拽到最近）。改层/矩阵时注意 Unity 2022.3 会按层名缓存回写 `TagManager.asset` —— **关掉 Unity 再改文件并复核**，代码一律用**层索引**不依赖层名。

**出生点**：DemoDay 的 `PlayerSpawner._spawnPoint` = **`(-15, 0.3, -30)`**（地面实测 y≈0.256；2026-10-17 第三次搬迁——(10,0.21,-40) → 此处，服务端布局同批收紧收进陆地）。约束是"到每个布局槽位的距离 > 该模板的感知半径 + 巡逻半径"⇒ 出生点挂机不会被任何怪发现；**离开出生点后照样会被追**。改点位或出生点时几何守卫会红 —— 那是设计，不是噪音。

**已知限制（不是 bug，改前先读）**：① 碰撞**只在客户端** ⇒ 改客户端仍可穿模，服务端也不校验重叠；② "**怪走进你身体**"挡不住（静态碰撞体挡不住会移动的怪，`CharacterController` 不会被 kinematic 碰撞体推动），零速度玩家最坏交叠约 0.48m（已量化）；③ ORCA 是**纯局部**避让：不解决"被挤进死角"，对称多体挤压仍有约 5% 视觉交叠。

---

## Remote Entity Presentation (SyncLayer)

`Assets/Scripts/Hotfix/GameSystems/SyncLayer/` 负责**远端实体的渲染**（本地角色走 3C 那条路，两者别混）：

| 实体 | 走哪条 | 退化路径 |
|---|---|---|
| 玩家 + bot（`EntityKind.Player` / `PlayerBot`） | **同一条**：外观目录 `_appearances`（留空则退回 `_playerAvatarPrefab` 单条默认外观）→ 真模型 + `RemoteEntityAnimator`（走/跑动画由渲染位移反推的速度驱动） | 两者都空 ⇒ 胶囊占位 |
| 怪物（`EntityKind.Npc`） | `_npcPrefabs`（templateId → 预制体）→ 真模型 + `NpcEntityAnimator` + `NpcBodyCollider` | 目录未命中 ⇒ 球体占位 |

- **bot 不再是胶囊**（D-P5-7 的"渲染成占位"已被实测推翻）：bot 与真人同一条真模型路径——AOI（80m）下同屏数由**局部密度**决定（`--bots 100` 撒在 ±500m 时同屏峰值只有 5~6 个），成本由分档兜底
- **表示分级**（`RemoteTierPolicy` + `RemoteEntityRegistry.SetTier`）：近距离 **Full** = 真模型（惰性实例化；降/升档 = 整棵 `SetActive`）；远距离 **Proxy** = 代理（条目配了 `Proxy` 就用它，否则运行时胶囊；整棵剥碰撞体）。规则 = 进入 60m / 退出 69m（迟滞）+ 每帧 Full 预算 `MaxFull`（最近优先）
- 升档要**当场回放锁存状态**（`Apply(view.ActionState, view.Dead)`）：`m_KeepAnimatorStateOnDisable: 0` 会重置 Animator，只"失效去重"会让死亡/受击中的实体先站直最多 264ms
- **URL 旋钮**（WebGL 出包没有 Inspector，手机实测靠它扫参）：`?full=<N>` 覆盖预算、`?fulldist=<米>` 覆盖进入距离、`?quality=mobile` 强制移动质量档（**别名无条件生效**——iPad 桌面 UA 下 `isMobilePlatform` 与 `deviceType` 两个自动判据都失效）
- **调试浮层**（`_showDebugOverlay`）第二行是实测读数：`Full=12 Proxy=8 B=20  Q=2  FPS=42`（B=预算、Q=质量档）——**竖屏约 390px 也必须完整可见**（`FormatTierReadout` 有内容与长度守卫）

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
- `Core/UIManager` - UI管理器
- `Core/UIPanel` - 面板基类
- `Core/ScreenAdapter` - 屏幕适配
- `Animation/UIAnimation` / `Animation/SequenceBuilder` / `Animation/UIAnimPreset` - 动画系统
- `Animation/UITweenExtensions` - 补间动画扩展
- Panels（3 个，全部经 `UIManager.RegisterFromPrefab` 从 `Assets/Resources/Prefabs/UI/` 加载，
  **不在场景里**）：`Panel/CharacterSelect/CharacterSelectPanel`、`Panel/HUD/PlayerHudPanel`、`Panel/HUD/TargetPanel`
- 已无 `UIDataBinding` / `ViewModelBase` / Loading / Toast / Tips / Confirm（这些类与组件均不存在）

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
- 版本: Git（**未启用 LFS**，二进制裸存）

---

## C# Syntax Rules

- **禁止**在 MonoBehaviour 非内部类上使用 GetComponent
- 简单字符串插值用 `+`，避免复杂表达式在 Debug.Log 中

---

## Current Status

- [x] KCP + protobuf networking
- [x] 3C System (Base/Hit 两层 FSM + StateCoordinator 攻击层 + CharacterController + AnimationDriver)
- [x] Skill System (技能Q/R + 突进组件 + 数据层)
- [x] Hit System (受击/击退/倒地/死亡)（**表现层只打通了受击/击退/防御受击/眩晕**：Hit 层无 `Knockback`/`Launched` 状态，`Death`/`Down`/`GetUp` 无入边转移，`Death`/`IsDead` 参数在 controller 里不存在 → 倒地/死亡/浮空无动画，属 backlog）
- [x] UI Framework
- [x] Bag System
- [x] Monster System (AI + Behaviour + Spawner)
- [x] Combat System (Hitbox + AttackShape)
- [x] Camera System (ThirdPersonCameraController)
- [x] VFX System (Weapon + Skill effects)
- [x] Nameplate System (Entity display)
- [x] NPC Mirror system（P5 怪物服务端化：服务器 MonsterModule 行为树驱动 + 客户端 Npc 快照视图）
- [x] Movement / Collision & Avoidance（服务端 ORCA 避让 + 客户端怪物碰撞体 + `NpcBody` 层 —— 见同名章节）
- [x] Remote Entity Presentation（外观目录 + 真人/bot 统一真模型路径 + 行走动画 + 距离/预算分档骨架 + `?full=` / `?fulldist=` / `?quality=mobile` 旋钮 —— 见同名章节；bot 不再是胶囊）