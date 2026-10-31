# 联网多人战斗一致性（演示级）设计

- 日期：2026-10-31
- 状态：待审阅
- 关联规格：`2026-08-27-network-sync-design.md`（P0–P5）、`2026-10-12-pvp-and-death-presentation-design.md`（PvP / 死亡复活 / 远程真角色）
- 目标场景：WebGL 面试演示，PC 与手机浏览器同场；2–10 个真人 + 服务端怪物；可选 `--bots 100` 压测

---

## 1. 背景与当前状态

四条验收目标：

1. 多人进入同一场景，能看到其他玩家角色。
2. 玩家能进行打斗（PvP）。
3. 玩家能一起打怪（PvE 协作）。
4. 各玩家看到的怪、其他玩家行为基本一致，有轻微延迟。

代码与既有规格核对后的现状：

| 目标 | 已成立 | 主要缺口 |
|---|---|---|
| 多人同场景 | 登录 → `WorldSnapshot` → AOI → 远程真角色 + 133ms 插值；断线重连 | 远程玩家只有跑/死表现；无名字/血条；攻击/技能/受击/格挡动作不同步；技能 VFX 只在本机；所有玩家出生在原点 |
| 玩家互打 | 服务端权威伤害、暴击/格挡减免、8 秒死亡复活、两端死亡可见 | 友伤常开且无目标选择；对手看不到攻击/受击/格挡动作；远程玩家无血条；无击杀反馈 |
| 一起打怪 | 服务端行为树怪物、共享权威 HP、所有人可伤害、怪物攻击广播、受击/死亡动作状态、30 秒重生 | 联网怪物无血条/名字/目标面板；队友伤害与命中特效第三人不可见；仇恨只认视野内最近玩家 |
| 行为一致 | 时钟同步、15Hz 快照、133ms 插值、AOI 分带、死亡/复活、怪物动作状态 | 玩家动作状态恒 0；格挡状态不在快照；远程 HP 不在快照；`DamageResult` 只发给施法者+受击者；远程投射物命中点与服务器不一致 |

既有 PvP 规格已经打通“互相打死 + 死亡复活 + 远程真角色”，本规格不重建这些，只补表现同步、状态可见性与目标选择。

---

## 2. 目标与成功判据

### 2.1 目标

1. **所有在场真人玩家**都看到彼此的攻击、受击、格挡动作与技能特效，而不是只有参战双方。
2. 每个真人玩家与联网怪物都有**权威 HP 血条与名字**；选中目标时目标面板显示权威 HP。
3. 远程玩家与怪物的 HP、死亡/复活、格挡姿态**从服务端状态同步**，迟到者与丢包后也能自愈。
4. 玩家可以**显式锁定目标**（真人或怪物），攻击发送 `TargetPlayerId`，不再只能依赖服务端自动选最近。
5. 远程投射物的**命中/停止视觉**与服务器裁决对齐：火球在服务端命中点爆炸，剑气被格挡时在停点停下。
6. 单机模式零影响；既有 EditMode / Server.Tests / PlayMode 全绿。

### 2.2 演示级一致（本规格的验收档位）

- 不做阵营/计分/击杀播报/仇恨表/唯一出生点/玩家碰撞。
- 只给**真人玩家（`EntityKind.Player`）与怪物（`EntityKind.Npc`）**加名牌、血条、目标锁定；`PlayerBot` 不加（`--bots 100` 时避免 100 套 UGUI）。
- 远程玩家受击闪白、武器拖尾不在本批；以受击动作 + 火花 + 飘字为一致性下限。

---

## 3. 范围边界

### 做

- 协议纯追加：快照 HP/MaxHP/IsDefending；`CastBroadcast` 落点/蓄力。
- 服务端：快照带 HP/防御；受伤/举盾立即下发；`DamageResult` 扩播给 AOI 观察者；登录名字名册。
- 客户端：
  - StateMachineBehaviour 按 Animator 注册回调，隔离本地/远程。
  - `RemoteEntityAnimator` 表现仲裁器（快照状态 vs 瞬时事件分离）。
  - 与本地职业无关的技能表现目录 + 远程施法表现（攻击动画、技能 VFX、投射物视觉）。
  - 远程实体 HP/名字/血条/只读 `ITargetable`。
  - 目标锁定与目标面板/选中环贯通。
  - 远程投射物命中/停止对齐。

### 不做

- 阵营、PvP 开关、计分、击杀播报。
- 仇恨表、目标迟滞（怪物仍按视野内最近存活玩家选目标）。
- 唯一出生点、玩家之间碰撞、玩家 vs 怪物服务端碰撞。
- 蓄力起手姿态同步、击退/浮空、远程角色受击闪白/武器拖尾。
- 掉落、经验、贡献统计。
- 协议版本号提升（纯追加字段，旧客户端忽略新字段）。

---

## 4. 核心决策

| # | 决策 | 理由 |
|---|---|---|
| **D1** | **状态 + 事件双通道**：HP/防御进快照（状态）；攻击/受击/技能用现有消息（事件） | 迟到者与丢包后自愈；瞬时表现不占用每帧快照；与既有 `Dead`/`ActionState` 的做法一致 |
| **D2** | `EntitySnapshot` 追加 `Hp`(13)/`MaxHp`(14)/`IsDefending`(15)，`EntityFieldMask` 追加 `Health=1<<7` / `Defending=1<<8` | 纯追加；旧客户端忽略；每帧置位（存活/血量是状态不是事件） |
| **D3** | `DamageResult` 收件人扩为：施法者 ∪ 受击者 ∪ 施法者观察者 ∪ 每个受击者观察者，`HashSet` 去重 | 第三人也能看到伤害数字/受击表现；不改变谁掉血 |
| **D4** | `CastBroadcast` 追加 `ImpactX/Y/Z`(10/11/12)、`ChargeProgress`(13) | 远程陨石/蓄力火球需要落点与视觉缩放；不改变判定 |
| **D5** | 远程玩家攻击**收到 `CastBroadcast` 立即播放**，不做未来排程；怪物保留现有 `NpcAttackSchedule` | 20002/20003/20004 的 `HitDelaySeconds=0`，排到未来会出现“先受击后出手”；立即播放保证攻击先于命中结果 |
| **D6** | `RemoteEntityAnimator`（仅玩家类视图）把**快照状态**与**瞬时事件**分开：只吃 `Dead` + `IsDefending`，忽略玩家恒 0 的 `ActionState`；怪物仍由 `NpcEntityAnimator` 按快照 `ActionState/Dead` 驱动 | 否则每条快照的 `Apply(0,false)` 会在 66ms 内清掉远程玩家受击动画 |
| **D7** | 用 **`AnimationCallbackRegistry` 按 Animator 注册回调**，替代三个 StateMachineBehaviour 的静态回调/静态标志 | 远程角色的 `Sys3CEntry.Start` 被禁用、从不创建 FSMManager；注册表让远程 Animator 查不到条目→天然不触发本地 FSM，同时修掉现有远程受击/死亡提前完成本地受击 FSM 的缺陷 |
| **D8** | 技能表现使用**与本地职业无关的 `SkillPresentationCatalog`**，只收 7 个同步玩家技能 | 本地 `SkillCoordinator` 只有本职业技能；远程法师的技能在战士客户端查不到 |
| **D9** | 名牌/血条/目标锁定只给 `Player` 与 `Npc`；`PlayerBot` 排除 | `--bots 100` 时不给 100 个 bot 加 UGUI；bot 是压测实体 |
| **D10** | 目标切换输入用 **PC `T` 键 + 移动端 UI 按钮**，不用 Tab | WebGL 下 Tab 可能被浏览器抢走焦点；接入现有模态输入门禁 |
| **D11** | 远程投射物视觉按 `(casterId, skillId)` 跟踪；收到远程 `DamageResult` 时用 `HitX/HitZ` 引爆火球、用 `StopX/StopZ` 停住被格挡的剑气 | 远程怪物视图不注册 `PhysicsRegistry`，现有火球视觉会穿过怪物/落地爆炸；剑气停点只服务本地施法者 |
| **D12** | 名字用现有 `PlayerEnterRoom` 做名册：新玩家登录时把房间内已有玩家的条目发给本人；客户端只取名字，忽略位置 | 不新增消息；`RoomSync` 已退役；名字是静态数据，不需要进每帧快照 |
| **D13** | 不升 `ProtocolVersion` | 纯追加字段对旧客户端非破坏；两端同批部署 |

---

## 5. 架构

```
[协议]
  EntitySnapshot  += Hp / MaxHp / IsDefending   (mask Health / Defending)
  CastBroadcast   += ImpactX/Y/Z / ChargeProgress

[服务端]
  PlayerState.Hp / MaxHp / IsDefending
      ├─ AoiBroadcaster    → 快照带 HP/防御（每帧掩码置位）
      ├─ AoiBandScheduler  → HP/防御变化立即下发
      ├─ CastHandler       → DamageResult 扩播给双方观察者（去重）
      └─ LoginHandler      → 名字名册（复用 PlayerEnterRoom）

[客户端]
  AnimationCallbackRegistry
      └─ StateMachineBehaviour 按 Animator 查回调；远程 Animator 无条目→不触发本地 FSM

  RemoteEntityRegistry / RemoteEntityView
      ├─ 快照 → Hp/MaxHp/IsDefending → 名牌+血条 / NetworkTargetable
      ├─ 玩家快照只驱动 Dead + IsDefending；怪物快照驱动 ActionState
      └─ 升档 Full → RemoteEntityAnimator.Reapply()

  RemoteEntityAnimator（表现仲裁器，仅玩家类视图）
      ├─ 快照：Dead / IsDefending
      └─ 事件：PlayAttack / PlayHit / Tick 计时
      优先级：Dead > Hit > Attack > Defend > Locomotion

  NpcEntityAnimator（怪物，现有路径不变）
      └─ 快照：ActionState / Dead

  RemoteCastPresenter
      ├─ CastBroadcast → 立即 PlayAttack + CastVFX
      ├─ 投射物视觉按 (casterId, skillId) 跟踪
      └─ DamageResult → 火球在 HitX/Z 爆炸、被格挡剑气在 StopX/Z 停住

  NetworkTargetSelector
      └─ T/按钮 → 锁定 Player/Npc → TargetPanel + SelectionRing + CastRequest.TargetPlayerId

  CombatNetworkBridge
      ├─ CastBroadcast → 宿主路由（NPC 排程 / 玩家 presenter）
      ├─ DamageResult → 本地玩家 ApplyServerDamage；远程玩家 PlayHit；远程怪物沿用 ActionState
      └─ 远程受击目标 NetworkTargetable 即时更新 HP
```

### 组件职责

| 组件 | 位置 | 职责 |
|---|---|---|
| `AnimationCallbackRegistry` | `Sys3C/Animation/StateBehaviours/` | Animator → 动画完成回调/受击完成标志的注册表 |
| `RemoteEntityAnimator` | `SyncLayer/` | 远程玩家 Animator 的唯一驱动：快照状态 + 瞬时事件 + 层仲裁 |
| `SkillPresentationCatalog` | `Skills/Data/`（ScriptableObject） | 7 个同步技能的表现数据（trigger/clip/VFX/投射物配置） |
| `RemoteCastPresenter` | `SyncLayer/` | 远程玩家施法表现与远程投射物视觉跟踪 |
| `NetworkTargetable` | `SyncLayer/` | 远程实体的只读 `ITargetable` 适配器 |
| `NetworkTargetSelector` | `SyncLayer/` | 目标锁定/切换/有效性检查/UI 绑定 |
| `PlayerNameRegistry` | `SyncLayer/` | `playerId → 显示名`，由 `PlayerEnterRoom/PlayerLeaveRoom` 维护 |
| `INetworkLabelSink` + 实现 | `SyncLayer/` + `Nameplate/` | 远程实体名牌/血条注册与更新（测试可注入 null） |

---

## 6. 协议

### 6.1 `EntitySnapshot`

文件：`Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs`

```csharp
// EntityFieldMask 追加（只加不删）
Health = 1 << 7,      // Hp / MaxHp 有效
Defending = 1 << 8,   // IsDefending 有效

// EntitySnapshot 追加
[ProtoMember(13)] public float Hp { get; set; }
[ProtoMember(14)] public float MaxHp { get; set; }
[ProtoMember(15)] public bool IsDefending { get; set; }
```

- 既有 tag 1–12 不动；13/14/15 空闲。
- `AoiBroadcaster` 每帧置位 `Health | Defending`（与 `Dead` 同惯例）。

### 6.2 `CastBroadcast`

文件：`Assets/Scripts/AOT/Protocol/Messages/Combat.cs`

```csharp
[ProtoMember(10)] public float ImpactX { get; set; }
[ProtoMember(11)] public float ImpactY { get; set; }
[ProtoMember(12)] public float ImpactZ { get; set; }
[ProtoMember(13)] public float ChargeProgress { get; set; }
```

- 既有 tag 1–9 不动。
- `CombatResolver.Decide` 构造 `CastBroadcast` 时从 `CastRequest` 原样搬运落点/蓄力。
- 不新增 `MessageId`；不升 `ProtocolVersion`。

---

## 7. 服务端

### 7.1 快照带 HP/防御

文件：`Server/Aoi/AoiBroadcaster.cs`

- `BuildForObserver` 的 `EntitySnapshot` 填充：
  - `Hp = entity.Hp`
  - `MaxHp = entity.MaxHp`
  - `IsDefending = entity.IsDefending`
- `FieldMask` 追加 `EntityFieldMask.Health | EntityFieldMask.Defending`。
- 玩家与怪物共用 `PlayerState`；怪物 HP/`MaxHp` 由 `EntityStore.AddNpc(..., config.hp, ...)` 初始化。

### 7.2 变化立即下发

文件：`Server/Aoi/AoiBandScheduler.cs`

- `Slot` 增加 `Hp` / `MaxHp` / `IsDefending`。
- `ShouldSend(...)` 增加这三个参数；任一变化 → 立即返回 `true`（与 `ActionState/Dead` 翻转同语义）。
- 静止实体的 4Hz 保活逻辑不变；新观察者首帧仍拿到全字段快照。

### 7.3 `DamageResult` 扩播

文件：`Server/Handler/CastHandler.cs`

`BroadcastDamageAsync` 的收件人集合：

```
hit targets（玩家才有 session）
∪ 施法者（玩家施法时）
∪ 施法者观察者：
    玩家施法 → GetVisiblePlayers(casterId)
    怪物施法 → GetObserversOf(casterId)
∪ 每个受击者的观察者：GetObserversOf(targetId)
```

- `HashSet<long>` 去重，每个会话只发一次。
- `GetObserversOf` 是 O(N²)/次（`Server3C` 注释已登记）；本批按 demo 规模接受，实体量上升时再换反向索引。
- 不改 `CombatResolver` 的候选集、命中几何、扣血与冷却。
- `DamageResult` 内容不变；观察者只用于表现，不会结算远程伤害。

### 7.4 登录名字名册

文件：`Server/Handler/LoginHandler.cs`

- 新玩家加入房间后，遍历 `room.GetAllPlayerStates()`，跳过本人，向**新玩家会话**逐条发送 `PlayerEnterRoom`（名字来自 `PlayerState.PlayerName`；位置字段可为 0，客户端约定忽略）。
- 原有“向其他玩家发送新玩家 `PlayerEnterRoom`”保留。
- 不新增协议消息；`RoomSync` 维持退役。

### 7.5 服务端不变量

- 伤害权威仍只在 `CombatResolver`；客户端不结算远程伤害。
- 怪物 AI/仇恨/攻击节奏不变。
- 所有新字段只加不删；旧客户端可忽略。

---

## 8. 客户端

### 8.1 StateMachineBehaviour 隔离（前置任务）

文件：

- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/AttackStateBehaviour.cs`
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/BaseStateBehaviour.cs`
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/HitStateBehaviour.cs`
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs`
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- 新增 `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/AnimationCallbackRegistry.cs`

#### 问题

三个 StateMachineBehaviour 使用**静态回调/静态标志**；控制器资产被本地与远程角色共用，远程角色播 Hit/Attack 层时会调用本地玩家的 `FSMManager`：

- 远程攻击 → 本地 `CleanupSkillAnimation()`，可能打断本地施法。
- 远程受击/死亡 → 改写 `HitStateBehaviour` 的静态 `_hasTriggeredHitComplete/_lastNormalizedTime`，导致本地受击提前完成或被吞。

远程角色的 `Sys3CEntry.Start` 被 `RemoteEntityAnimator.Awake` 禁用，**远程角色不会创建 FSMManager**；因此只有本地玩家会注册回调。

#### 方案

新增 `AnimationCallbackRegistry`（纯 C# 静态表）：

```csharp
public static class AnimationCallbackRegistry
{
    internal sealed class Entry
    {
        public Action<string> Attack;
        public Action<string> Hit;
        public bool HasTriggeredHitComplete;
        public float LastNormalizedTime;
    }

    public static void Register(Animator animator, Action<string> attack, Action<string> hit);
    public static void Unregister(Animator animator);
    internal static bool TryGet(Animator animator, out Entry entry);
    public static void ClearForTests();
}
```

- `FSMManager` 构造时 `AnimationCallbackRegistry.Register(_animator, HandleAnimationCompleted, HandleHitAnimationCompleted)`，替换三处 `SetCallback`。
- `Sys3CEntry.OnDestroy` 调用 `AnimationCallbackRegistry.Unregister(Animator)`。
- `RemoteEntityAnimator.Awake` 防御性调用 `Unregister(animator)`（远程从未注册也应是无害的）。
- 三个 behaviour 删除静态回调/静态标志；`OnStateEnter/Update/Exit` 首行 `if (!AnimationCallbackRegistry.TryGet(animator, out var e)) return;`；`HitStateBehaviour` 的触发标志与 normalizedTime 存在 `Entry` 上。
- 远程 Animator 无条目 → 不触发任何游戏逻辑；Animator 本身照常播放。

#### 测试

- 同一控制器下本地 + 远程两个 Animator：远程播 Hit/Attack/Death，不推进本地 `HitFSM`、不调用本地 `OnAttackAnimationCompleted`。
- 本地自己的动画完成回调仍正常。
- `Sys3CEntry` 销毁后注册表无残留；`ClearForTests` 清理。
- 既有直接调用 `SetCallback` 的测试改为 `AnimationCallbackRegistry.Register`；如需兼容包装，只用于测试，生产路径不再使用静态回调。

### 8.2 `RemoteEntityAnimator` 表现仲裁器

文件：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs`

> 仅玩家类视图（真人 + bot 的 `Player`/`PlayerBot` 视图）。怪物视图由 `NpcEntityAnimator` 按快照 `ActionState/Dead` 驱动，本组件不参与。

#### 问题

- 现有 `Apply(actionState, dead)` 只管理 Hit 层；新增攻击/格挡若散着调，会互相覆盖。
- 玩家快照 `ActionState` 恒 0；每条快照的 `Apply(0,false)` 会把事件驱动的受击动画清掉。
- `SetTrigger` 在层权重为 0 时被 Animator 静默丢弃。
- Proxy→Full 升档时 `m_KeepAnimatorStateOnDisable=0` 会重置层权重；现有 `SetTier` 只重放 `Apply(dead)`。
- 控制器可能缺参数/层；`SetTrigger` 缺参数会逐帧刷错误日志。
- 部分外观预制体 `applyRootMotion=1`，会与插值位置打架。

#### 方案

**状态来源（玩家类视图）：**

| 来源 | 处理 |
|---|---|
| 快照 `Dead` | 死亡/复活 |
| 快照 `IsDefending` | 格挡姿态 |
| 快照 `ActionState` | **忽略**（玩家恒 0） |
| `CastBroadcast` | `PlayAttack` |
| `DamageResult` | `PlayHit(blocked)` |

**API：**

```csharp
internal void ApplySnapshotState(bool dead);
internal void PlayAttack(string trigger, float duration);
internal void PlayHit(bool blocked, float duration);
internal void SetDefending(bool defending);
internal void Tick(float deltaTime);      // 测试可直接推进
internal void Reapply();                  // 升档/OnEnable 重放 dead + defending
internal void ResetLayerWeights();        // 保留现有语义
internal void InvalidateDedup();
```

**仲裁优先级**：`Dead > Hit > Attack > Defend > Locomotion`。

- `RefreshLayers()` 统一写：
  - Dead：Hit 层权重 1、`HitState=Death`；Attack/Defend 权重 0。
  - Hit：Hit 层权重 1、`HitState = blocked ? DefendHit : Hit`；Attack/Defend 0。
  - Attack：Attack 层权重 1、`AttackState=Attacking`、设置 trigger；Hit/Defend 0。
  - 否则：Hit/Attack 0、`AttackState=Idle`、Defend = `_defending ? 1 : 0`、`IsDefending` bool 同值。
- `PlayAttack`：先清旧 trigger，再设层权重，最后 `SetTrigger`（权重 0 时 trigger 会被丢）。
- `Tick`：递减攻击/受击计时；到期清意图、`ResetTrigger`；`RefreshLayers`。
- `ApplySnapshotState(bool dead)`：
  - `dead=true`：清空瞬时计时；进入 Death。
  - `dead=false` 且此前死亡：清 Hit；`Reapply` 恢复 `IsDefending`。
  - 玩家快照没有 `ActionState` 语义，调用方不要传；旧 `Apply(actionState, dead)` 只保留 `dead` 语义，既有测试若断言 `ActionState=Hit` 需改为 `PlayHit`/`ApplySnapshotState`。
- `Reapply`：重放 `_dead` + `_defending`；清攻击/受击计时（升档时不重播进行中的剪辑）。
- `Awake`/`ResolveAnimator`：`animator.applyRootMotion = false`；缓存 `layerCount` 与参数哈希集合；缺 `AttackState`/`HitState`/`IsDefending`/trigger/层时安全降级并只告警一次（照抄 `NpcEntityAnimator.HasDriverParameters` 的范式）。
- `OnDisable/OnEnable`：清瞬时计时；`OnEnable` 调 `Reapply`。
- 保留 `LastHitState` / `LastBaseState` / `LastBlend` / `LastLocomotionSpeed` 观察面，既有测试不回归。

**计时来源**：`PlayAttack` 的 duration 由 `SkillPresentationCatalog` 的 `SkillData.CastClip/ReleaseClip.length` 提供；缺失时用 0.8s 保守值。`PlayHit` 用 0.5s 保守值，后续可按 Hit 层当前 clip 长度微调。

#### 测试

- EditMode（`Tick` 直接推进）：
  - 优先级：死亡中受击/攻击不出现；受击覆盖攻击；攻击覆盖格挡；恢复后格挡姿态回来。
  - 计时到期后层权重/参数复位；trigger 被 reset。
  - 缺参数/缺层安全降级；`applyRootMotion == false`。
  - 升档 `Reapply` 恢复死亡/格挡。
- PlayMode：远程玩家被击中后跨 2 个快照周期仍有受击表现（D6 回归）。

### 8.3 技能表现目录

文件：新增 `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SkillPresentationCatalog.cs` + 资产

- ScriptableObject，序列化 `SkillData[]`，只收录 `CombatRules` 的 **7 个玩家技能**（括号内为资产现有 `AnimatorTrigger`）：
  - 10001 `BA_Combo1`（`Attack`）
  - 10002 `BA_Heavy`（`HeavyAttack`）
  - 20001 `Instant_SkillQ`（`SkillQ`）
  - 20002 `Spin_SkillR`（`SkillR`）
  - 20003 `Fireball_SkillQ`（`Fireball`）
  - 20004 `ChargeFire_SkillR`（`ChargeBlast`）
  - 30001 `Meteor_Ultimate`（`Ultimate`）
- 资产位于 `Assets/PreRes/SkillsCfg/`；`SwordQi_Heavy`、`MeteorConfig_Fire` 是配置资产，由对应 `SkillData` 引用，不单独进目录。
- 提供 `TryGet(int skillId, out SkillData data)`；缺条目时远程表现跳过并只告警一次。
- **不查**本地 `SkillCoordinator`（它只有本职业技能）；50001–50004 是服务端怪物技能，不进目录。
- 完备性测试：7 个 `CombatRules` 玩家技能都能在目录中找到，且 `AnimatorTrigger` 非空（上表括号值）。若某条资产为空，先补资产，不允许远程静默无动作。

### 8.4 远程施法表现

文件：新增 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs`

#### 触发与路由

- 订阅 `CombatNetworkBridge.OnCastBroadcast` / `OnDamageResult`（主线程，`CombatNetworkBridge.Update` 内）。
- 本地施法者（`CasterPlayerId == LocalPlayerId`）跳过：本地技能表现已由 `SkillExecutor` 播放。
- 怪物施法者（视图 `Kind == Npc`）跳过：由现有 `NpcAttackSchedule`/`NpcEntityAnimator` 负责。
- 只处理 `Player`（真人）；`PlayerBot` 不施法，无需处理。
- 视图不存在（快照未到）→ 丢弃；视图生命周期由快照独占。

#### 立即播放（D5）

- 收到远程玩家 `CastBroadcast` 后**立即**：
  - `view.Animator?.PlayAttack(catalog.AnimatorTrigger, clipDuration)`。
  - 在 `view.Position + up*offset`、`view.Rotation` 实例化 `PresentationBlock.CastVFX`（若配置）。
  - 若是投射物技能，立即生成投射物视觉并登记到跟踪表（见下）。
- 不按 `ServerTimestamp` 排到未来；保证“攻击先于命中结果”，且无需暴露服务器时钟。
- `CastVFX` 按世界空间生成；若某个技能的特效必须挂在武器/手部骨骼上，本批登记为美术调整项，不新建挂点系统。

#### 投射物视觉对齐（D11）

- `RemoteCastPresenter` 维护 `List<TrackedProjectile>`：
  - `CasterId`、`SkillId`、类型（Fireball / SwordQi）、`FireballProjectile`/`SwordQiProjectile` 引用。
- 生成：
  - 10002 剑气：`SwordQiSpawner.Spawn(...)` 改为返回 `SwordQiProjectile`；用视图当前位置/朝向。
  - 20003 火球：`FireballSpawner.Spawn(...)` 改为返回 `FireballProjectile`；用视图当前位置/朝向。
  - 20004 蓄力火球：`CastVFX` 在施法者处；释放/爆炸表现放在 `ImpactX/Y/Z`，不跟踪飞行体。
  - 30001 陨石：用 `SkillData.Meteor` 调 `MeteorSpawner.Spawn(...)`，落点取 `ImpactX/Y/Z`；预警/落地由 Meteor 自身负责。
- 收到 `DamageResult`：
  - 火球（20003）：若 `Hits.Count > 0`，取第一个 `HitEntry.HitX/HitZ`，调用 `FireballProjectile.ExplodeAt(point)`；若没有命中（空放/落地），让视觉自行落地/超时。
  - 剑气（10002）：若 `ProjectileStopped`，用 `StopX/StopZ` 调用 `SwordQiProjectile.StopAtPoint(point)`；未格挡则照常穿过（线形技能本就应该穿过）。
  - 其他投射物：不处理。
- 在线伤害短路不变：`FireballProjectile.ApplyDamage`/`SwordQiProjectile.DetectAndDamage` 在 online 模式直接 return；远程视觉不会造成双倍伤害。
- 幂等：`FireballProjectile.ExplodeAt` 入口检查 `_released`；`SwordQiProjectile.StopAtPoint` 已幂等。
- 跟踪表在视图移除/断线时清空；每帧/每次结果后清理 `IsActive == false` 的项。
- 匹配规则：同 `(casterId, skillId)` 有多个在飞视觉时，选**最早生成且仍活跃**的一项（服务器按施法顺序下发结果）。
- 20003 的 `HitDelaySeconds=0`：旁观者可能看到火球生成后几乎立刻在命中点爆炸，这是服务端无飞行延迟的直接结果，登记为已知表现。
- 测试缝：`IRemoteProjectileVisual { bool IsActive; void ExplodeAt(Vector3); void StopAtPoint(Vector3); }`；真实实现包装 `FireballProjectile`/`SwordQiProjectile`，EditMode 注入假实现。

#### API 改动（最小）

- `SwordQiSpawner.Spawn` 返回 `SwordQiProjectile`（现有调用方忽略返回值即可）。
- `FireballSpawner.Spawn` 返回 `FireballProjectile`。
- `FireballProjectile` 增加 `public bool IsActive => !_released;` 与 `public void ExplodeAt(Vector3 point)`（内部 `Explode(null, point)`，入口 `if (_released) return;`）。
- `SwordQiProjectile.IsActive` / `StopAtPoint` 已存在，不改。
- `CastVFX` 按现有 `ReleaseVFX` 的做法一次性 `Instantiate` + `Destroy(vfx, 1.5f)`；不在本批引入新池。若 WebGL 压测出现 GC 抖动，后续再池化（性能风险登记）。

#### 测试

- EditMode：用 `IRemoteProjectileVisual`（包装 Fireball/SwordQi 的最小接口）注入假实现，断言：
  - 远程 CastBroadcast 触发攻击表现与视觉生成；
  - 火球 DamageResult 命中 → 在 `HitX/HitZ` 爆炸一次；
  - 剑气 `ProjectileStopped` → 在 `StopX/StopZ` 停住一次；
  - 本地施法者/怪物施法者被跳过；视图移除后跟踪表清空。
- PlayMode：三客户端旁观者收到远程火球/剑气表现计数（暴露只读计数器）。

### 8.5 远程实体 vitals、名字与可选中

文件：

- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`
- 新增 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkTargetable.cs`
- 新增 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/PlayerNameRegistry.cs`
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`
- `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateRenderer.cs`
- `Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs`

#### `RemoteEntityView` 扩展

```csharp
public float Hp { get; set; }
public float MaxHp { get; set; }
public bool IsDefending { get; set; }
public string DisplayName { get; set; }
public NetworkTargetable Targetable { get; }
```

#### `ApplySnapshot` 更新

- 按 `FieldMask` 守卫：`Health` 位存在才写 `Hp/MaxHp`，`Defending` 位存在才写 `IsDefending`（当前广播恒置位，守卫为未来部分快照留口）。
- `view.Targetable.SetVitals(Hp, MaxHp, Dead)`；HP 变化即时更新目标面板。
- `view.Animator?.ApplySnapshotState(snapshot.Dead)`（仅玩家类视图；`view.Animator` 为 null 的怪物视图走既有 `NpcEntityAnimator` 快照路径，不变）。
- Full 档时 `view.Animator?.SetDefending(snapshot.IsDefending)`。
- 标签：`_labelSink?.UpdateHp(viewId, percent)`；死亡时 0，复活时满。
- `SetTier` 升 Full：调用 `view.Animator.Reapply()` 与 `SetDefending(view.IsDefending)`。
- `Remove/Clear`：`_labelSink?.Unregister(viewId)`、`view.Targetable.MarkRemoved()`、清 presenter 跟踪（由宿主/registry 协调）。

#### `NetworkTargetable`

普通类实现 `ITargetable`：

- `DisplayName`、`Level=1`、`Portrait=null`、`HPPercent`、`CurrentHP`、`MaxHP`、`WorldPosition`、`SelectionRingYOffset=0.05f`。
- `CurrentHP`/`MaxHP` 取整、钳到 ≥0；`MaxHP<=0` 时 `HPPercent=0`。
- `WorldPosition` 返回缓存的 `LastPosition`（registry 每帧 `UpdateRender` 写入插值位置），**不访问已销毁的 Transform**。
- `SetVitals(hp,maxHp,dead)`：变化才触发 `OnHPChanged`；`dead` 由 false→true 触发一次 `OnDeath`，复活后重置。
- `SetDisplayName(name)`；`MarkRemoved()` 触发一次 `OnDeath` 并标记失效。
- 只读：不实现 `IDamageable`，本地伤害链永远够不到它。

#### `PlayerNameRegistry`

- 由 `ClientNetworkManager` 持有；订阅 `PlayerEnterRoom`（新玩家也会收到已有玩家条目）与 `PlayerLeaveRoom`。
- `TryGetName(long id, out string name)`；`OnNameChanged(long id, string name)` 事件。
- 断线/离开世界时清空。
- `ClientNetworkHost` 收到 `OnNameChanged` 后调用 `RemoteEntityRegistry.UpdatePlayerName(id, name)`：更新 `view.DisplayName`、名牌文本、`NetworkTargetable.SetDisplayName`。
- 名字截断到 16 字符；未知玩家回退 `玩家 {id}`；bot 回退 `Bot {id}`。

#### 名牌与血条

- `NameplateRenderer` 新增**独立的带血条模板**与第二对象池；现有 `Register` 模板与行为一行不动（单机怪物名牌零回归）。
- 结构：
  - Root：`VerticalLayoutGroup`（childAlignment=MiddleCenter，spacing=2，childControlWidth/Height=false）+ `ContentSizeFitter`；pivot (0.5, 1)，从头顶向下展开。
  - NameRow：沿用现有 `HorizontalLayoutGroup`（ClassIcon + NameText）。
  - HPBar：固定尺寸（如 60×6）；背景 `Image`（深色）+ 填充 `Image`（红色/绿色）。
- 填充：使用 **1×1 白色 sprite + `Image.fillAmount`**（有 sprite 时 `Filled` 可靠，且不触发布局重建）；1×1 sprite 在 `NameplateRenderer` 初始化时用 `Texture2D(1,1)` + `Sprite.Create` 创建一次并复用，不新增美术资产，`Cleanup` 时销毁；`Return` 时复位 `fillAmount=1`、颜色、RectTransform。
- `EntityDisplayManager` 增加 `RegisterWithHealth` / `UpdateHealth`；通过可选 `INetworkLabelSink` 注入，生产实现由 `ClientNetworkHost` 提供；测试传 null 不注册。
- 接口：

```csharp
interface INetworkLabelSink
{
    void Register(long entityId, Transform owner, string displayName);
    void UpdateName(long entityId, string displayName);
    void UpdateHp(long entityId, float percent);
    void Unregister(long entityId);
}
```
- 只为 `Kind == Player` 与 `Kind == Npc` 注册；`PlayerBot` 不注册。
- 怪物显示名：`NpcPrefabEntry.DisplayName`（新增字段）；为空回退 prefab.name；玩家显示名来自 `PlayerNameRegistry`。
- 名牌继续由现有 `Tick`（距离剔除/淡出/屏幕坐标）驱动；血条作为子节点跟随。

#### 怪物显示名数据路径

- `ClientNetworkHost.NpcPrefabEntry` 增加 `public string DisplayName;`。
- `ClientNetworkHost` 建立 `templateId → prefab` 与 `templateId → displayName` 两张表；`RemoteEntityRegistry` 构造增加**可选**参数，既有测试调用不需要改：

```csharp
RemoteEntityRegistry(Transform root,
    IReadOnlyList<AppearanceEntry>? appearances = null,
    IReadOnlyDictionary<int, GameObject>? npcPrefabs = null,
    IReadOnlyDictionary<int, string>? npcDisplayNames = null,
    INetworkLabelSink? labelSink = null,
    Func<long, string?>? playerNameResolver = null)
```

- registry 提供 `event Action<long>? ViewCreated` / `event Action<long>? ViewRemoved`，在创建、`Remove`、`Clear` 时触发；`RemoteCastPresenter` 订阅 `ViewRemoved` 清跟踪，`NetworkTargetSelector` 用它即时清锁。
- 场景 `DemoDay.unity` 的 4 个 `_npcPrefabs` 条目补名字，与 `Server/Config/monster_config.json` 的 `name` 对齐：1=Slime、2=Wolf、3=DogKnight、4=SpiderBoss。

### 8.6 目标锁定

文件：

- 新增 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkTargetSelector.cs`
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs`
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs`
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/SkillCastReporter.cs`
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`

#### 输入

- `InputManager` 新增 `IsTargetSwitchPressed()`：
  - PC：`Input.GetKeyDown(KeyCode.T)`。
  - 移动端：`_targetSwitchPressedFromUI` 标志（按钮 `onClick`/`PointerDown` 设置）。
  - 与攻击/技能一样受现有模态输入门禁约束。
- 新增“切换目标”UGUI 按钮，接入 `MobileInputBinder`/现有移动端按钮组；390px 竖屏安全区验证，不与摇杆/技能/攻击按钮重叠。
- **不使用 Tab**（WebGL 浏览器焦点风险）。

#### `NetworkTargetSelector`

- 由 `ClientNetworkHost` 在联网装配本地角色时添加/初始化；单机不创建。
- 依赖：`RemoteEntityRegistry`、本地角色 `Sys3CEntry`、`SelectionRing`、`CombatNetworkBridge`、`InputManager`。
- 候选：`Kind == Player || Kind == Npc`，排除 `PlayerBot`、死亡、自己；距离 ≤ `AcquireRange`（默认 15m）；位于角色前方 ±90°（或技能形状的锥/全角，取更宽者）。
- 切换：按 `T`/按钮在候选列表按距离排序后循环；锁定后绑定：
  - `TargetPanel.Bind(view.Targetable)` 并显示。
  - `SelectionRing.AttachTo(view.GameObject.transform, view.Targetable.SelectionRingYOffset)`。
- 有效性每帧检查：视图存在、未死、在 `DropRange`（默认 20m）内、本地玩家存活；失效 → 清锁、解绑、隐藏面板。
- `EnsureTargetFor(SkillData data)`：
  - 若已有有效锁定，保留。
  - 否则按该技能的形状/射程（`HitShapeConverter.GetShape(data)`；无形状用 AcquireRange/120°）选最近目标。
  - 若目标在施法瞬间已出射程/锥：清锁并回退 `LockedTargetId=0`（服务端自动目标），避免必空。
- 本地玩家死亡：清锁、禁用切换输入；复活后允许。
- `LockedTargetId` 供 `ClientNetworkHost` 在施法前读取。

#### 施法路径

- `ClientNetworkHost.OnLocalSkillCast`：
  1. `_targetSelector.EnsureTargetFor(skillData)`。
  2. `SkillCastReporter.ReportActivation(..., _targetSelector.LockedTargetId)`。
- `OnLocalChargeReleased`：释放时同样 `EnsureTargetFor`，再 `ReportChargeRelease(..., targetId)`。
- `SkillCastReporter` / `CombatNetworkBridge.NotifySkillCast` 增加 `long targetId` 参数；`CastRequest.TargetPlayerId = targetId`。
- AOE/投射物技能即使带 targetId，服务端按形状判定，忽略该字段；单目标技能走显式目标。
- 服务端 `HitResolver.Resolve` 的显式目标校验不变（距离 + 前方锥）。

#### 目标面板名字晚到

- 若目标面板已绑定且名册后到：重绑或更新 `TargetPanel` 的名字文本（增加 `SetName` 或重新 `Bind`）；不阻塞主流程。

### 8.7 `CombatNetworkBridge` 变更

- `NotifySkillCast(..., long targetId = 0)`：写 `CastRequest.TargetPlayerId`。
- `ApplyDamageResult`：
  - 本地受击者：维持现有 `_onDamageToLocalPlayer`（权威余血/格挡/来源）。
  - 远程玩家（`Kind == Player`）：`view.Targetable?.SetVitals(hit.TargetRemainingHp, view.MaxHp, dead: hit.TargetRemainingHp <= 0f)`；`view.Animator?.PlayHit(hit.IsBlocked, 0.5f)`；现有飘字/格挡事件保留。
  - `PlayerBot`：只保留飘字；不播 `PlayHit`（压测实体）。
  - 远程怪物：`view.Targetable?.SetVitals(hit.TargetRemainingHp, view.MaxHp, dead: hit.TargetRemainingHp <= 0f)`；维持现有 `MonsterTakeDamageEvent`；不调 `PlayHit`（快照 ActionState=Hit 负责）。
  - `ProjectileStopped`/`OnLineProjectileStopped`：保持本地施法者路径；`RemoteCastPresenter` 另订阅 `OnDamageResult` 处理远程视觉停点。
- 观察者现在也会收到 `DamageResult`；`IsLocalSource` 仍按 `SourcePlayerId == localId` 计算。本批**开启** `HitFeedbackSystem` 对远程来源的命中火花（删除其 `IsLocalSource` 早退）；`CameraShakeManager`/`HitStopManager`/`HitParticleController` 的本地守卫保持不变，避免别人打怪晃本地镜头。

### 8.8 `ClientNetworkHost` 接线与更新顺序

- 装配本地角色时：
  - 创建/初始化 `NetworkTargetSelector`（取 `GetComponentInChildren<SelectionRing>()`）。
  - 创建/初始化 `RemoteCastPresenter`（bridge、registry、localId、catalog、labelSink）。
  - `OnLocalSkillCast`/`OnLocalChargeReleased` 按 8.6 传入 targetId。
- 每帧顺序：
  1. `_manager.Update(dt)`（时钟/快照/插值，内部已 `registry.UpdateRender`）。
  2. `_combatBridge.Update()`（收 CastBroadcast/DamageResult；presenter 立即播放/生成）。
  3. `_remoteCastPresenter.Update(dt)`（清理失效投射物视觉）。
  4. `_targetSelector.Update()`（输入/有效性/UI 绑定）。
- 断线/离开世界：清 `RemoteCastPresenter`、`NetworkTargetSelector`、`PlayerNameRegistry`、registry 标签；摘除事件订阅。
- 单机模式：不创建 selector/presenter；标签 sink 为空。

---

## 9. 关键数据流

### 9.1 A 攻击怪物 M，B 旁观

1. A 施法 → `CastRequest(TargetPlayerId=M)` → 服务端 `CombatResolver.Decide`。
2. 服务端 `CastBroadcast` 发给 A 与观察者（B）。
3. B 的 `CombatNetworkBridge.OnCastBroadcast` → `RemoteCastPresenter`：A 的视图播攻击动画 + CastVFX + 投射物视觉。
4. 服务端判定 → `DamageResult` 发给 A、M（无 session）、A 的观察者、M 的观察者（B）。
5. B 的 bridge：M 视图飘字/火花，`M.Targetable` 用 `TargetRemainingHp` 即时更新血条。
6. 下一帧快照带 M 的权威 HP/ActionState=Hit；B 的怪物受击动画/血条与 A 一致。
7. 迟到者/丢包后，下一帧快照的 HP 纠正屏幕值。

### 9.2 A 攻击 B，C 旁观（PvP）

1. A 锁定 B（T/按钮）→ 攻击发送 `TargetPlayerId=B`。
2. 服务端显式目标命中 → `CastBroadcast` 给 A/B/C，`DamageResult` 给 A/B/C。
3. A/C 看到 B 的受击/格挡动作、飘字、火花、血条下降；B 本地走既有 `ApplyServerDamage`/HitFSM。
4. B 死亡：快照 `Dead=true` → A/C 看到倒地；B 收到 `RespawnNotice` 起身；A/C 快照 `Dead=false` → 起身/满血。

### 9.3 目标锁定

1. `T`/按钮 → `NetworkTargetSelector` 从候选循环选一个。
2. 绑定 `TargetPanel`（权威 HP）+ `SelectionRing`。
3. 攻击时 `EnsureTargetFor` → `CastRequest.TargetPlayerId`。
4. 目标死亡/出 AOI/超距 → 清锁、解绑、隐藏面板。

---

## 10. 错误处理与生命周期

| 场景 | 处理 |
|---|---|
| 快照先于视图/视图先于名册 | 视图用回退名；名册到达后 `UpdatePlayerName` 更新名牌/目标面板 |
| 远程施法广播先于视图 | `RemoteCastPresenter` 丢弃；视图由快照独占创建 |
| 技能不在目录 / trigger/参数缺失 | 跳过该技能的表现并只告警一次；不影响伤害/HP |
| 控制器层数/参数不足 | `RemoteEntityAnimator` 安全降级为“只渲染/只播 locomotion” |
| 视图移除 / AOI 出界 / 断线 | registry `Remove` 注销标签、`Targetable.MarkRemoved`、presenter 清跟踪、selector 清锁 |
| 本地玩家死亡 | selector 清锁并禁用切换；复活后恢复 |
| 目标在施法瞬间出射程/锥 | 清锁并回退 `TargetPlayerId=0`，避免必空 |
| 远程投射物视觉先落地 | 跟踪项失效即清理；`DamageResult` 到达时若无活跃视觉则跳过 |
| 火球重复引爆 / 剑气重复停 | `ExplodeAt`/`StopAtPoint` 幂等；跟踪表去重 |
| 域重载 / 场景切换 | `AnimationCallbackRegistry` 注销；presenter/selector/registry 清空；`ClearForTests` |
| 线程 | 所有 Unity API 只在主线程（`CombatNetworkBridge.Update`/`ClientNetworkHost.Update`）；网络回调只入队 |
| 协议旧客户端 | 新字段纯追加；旧客户端忽略；不升版本 |

---

## 11. 测试与验收

### 11.1 EditMode

- 协议往返：`EntitySnapshot` 的 HP/MaxHP/IsDefending 与掩码位；`CastBroadcast` 落点/蓄力。
- `AnimationCallbackRegistry`：本地 Animator 有回调；远程/未注册 Animator 不触发；注销/清理。
- `RemoteEntityAnimator`：仲裁优先级、计时复位、trigger reset、缺参数降级、`applyRootMotion=false`、升档重放、玩家视图只吃 `Dead/IsDefending`（怪物 `ActionState` 由 `NpcEntityAnimator` 现状回归）。
- `NetworkTargetable`：HP 事件、死亡一次/复活重置、销毁后位置缓存不抛异常。
- `NetworkTargetSelector`：候选筛选（bot/死亡/距离/角度）、循环、失效清锁、`EnsureTargetFor` 回退。
- `SkillPresentationCatalog`：7 个同步玩家技能全覆盖；`SwordQi_Heavy`/`MeteorConfig_Fire` 不作为独立条目。
- `RemoteCastPresenter`（假 `IRemoteProjectileVisual`）：火球 `HitX/HitZ` 引爆、剑气 `StopX/StopZ` 停住、本地/怪物施法跳过、视图移除清理。
- `NameplateRenderer`：带血条模板创建/更新/池复位；现有 `Register` 行为不变。

### 11.2 Server.Tests

- `AoiBroadcaster`：快照带 HP/MaxHP/IsDefending 与掩码位。
- `AoiBandScheduler`：HP/防御变化立即下发；静止保活不变。
- `CastHandler`：`DamageResult` 收件人含施法者/受击者/双方观察者且去重；怪物施法走 `GetObserversOf`；不改变扣血次数。
- `CastBroadcast`：落点/蓄力原样搬运。
- `LoginHandler`：新玩家收到已有玩家名册；其他玩家收到新玩家条目。
- 既有 Server.Tests 全绿（数量以当前分支为准，不写死）。

### 11.3 PlayMode

- 既有套件全绿（TwoClientWorldTests、CombatSyncTests、MonsterWorldTests、AoiWorldTests、RemoteRenderBranchTests、PlayerDeathRespawnTests、BlockMitigationE2ETests 等）。
- 新增三客户端 `ThreeClientCombatSyncTests`：
  - A 打 B：A/B/C 收到同一 `DamageResult`；C 的快照 HP 与 B 的 `TargetRemainingHp` 一致；C 的 `NetworkTargetable` HP 一致。
  - A 打怪物：C 看到怪物血条下降、受击表现计数增加；怪物死亡/重生在 C 端可见。
  - 远程攻击不打断本地施法（P0-1/D7 回归）。
- `RemoteEntityAnimator` 接受 `Tick` 的 PlayMode 用例：远程受击跨 2 个快照周期仍可见（D6 回归）。
- 目标锁定：发送 `TargetPlayerId` 后服务器命中的是锁定目标；切换目标生效。

### 11.4 浏览器 E2E / 人工验收

- 两个浏览器客户端 + 一个旁观者（或 A/B 双方 + 截图）：
  - 彼此可见真角色；名字/血条在头顶。
  - A 攻击 B：两端与旁观端都看到攻击动作、受击/格挡动作、伤害数字/火花；B 的 HP 条一致下降。
  - A/B 打同一只怪：两端 + 旁观端怪物血条一致；怪物攻击/受击/死亡/重生一致。
  - 火球在服务端命中点爆炸；被格挡的剑气在停点停下。
  - 目标环/目标面板在锁定/切换/死亡/出视野时正确显示/隐藏。
  - 390px 竖屏：移动端“切换目标”按钮不重叠、可点击。
  - 0 条 `NotSupportedException`/解码告警。
- `--bots 100`：不产生 bot 名牌/血条；帧率不塌；AOI 计数不塌。

### 11.5 带宽/性能

- `BandwidthBudgetTests` 加入 HP/MaxHP/IsDefending 字段；按 2–10 真人 + 4 怪物场景量测。
- 快照每实体约 +12B；若超标，优先只对“真人 + 怪物”发送 HP（bot 仍可省略），不引入新的压缩格式。
- 远程名牌/血条只对真人+怪物；数量受 AOI 限制。
- `CastVFX` 一次性实例化 + 1.5s 销毁；若 WebGL GC 抖动明显，再池化（登记为后续优化）。

---

## 12. 风险

| # | 风险 | 应对 |
|---|---|---|
| R1 | 快照变大 / 广播变多 | `BandwidthBudgetTests` 门禁；AOI 限制收件人；必要时对 bot 省略 HP |
| R2 | StateMachineBehaviour 注册表重构回归 | 先做 D7 并跑本地/远程双 Animator 用例；再动远程攻击 |
| R3 | 远程 Animator 层/参数/升档生命周期 | 统一 `Reapply`/`RefreshLayers`；缺参数降级；PlayMode 回归 |
| R4 | 远程投射物匹配错/重复停 | `(casterId, skillId)` + 最早活跃；幂等；跟踪表清理 |
| R5 | 技能目录拉高 WebGL 包体 | 只收 7 个同步玩家技能；若超标改用 Addressables 标签加载 |
| R6 | 场景/资产编辑被静默回滚 | 按项目既有资产落盘流程：保存后 `assets-refresh` 再读回核对；记录 md5 |
| R7 | 移动端目标按钮布局 | 390px 竖屏验收；模态输入门禁；不遮挡摇杆/技能 |
| R8 | 远程名字名册晚到 | 回退名 + `OnNameChanged` 更新；不阻塞视图创建 |
| R9 | 怪物仇恨仍是最近玩家 | 本批不改；在 README/演示话术登记为已知限制 |
| R10 | `CastVFX` 一次性 `Instantiate/Destroy` 在 WebGL 造成 GC 抖动 | 低频技能；本批沿用现有 `ReleaseVFX` 的 1.5s 自动销毁模式，若压测抖动明显再加按 prefab 的小池 |

---

## 13. 任务拆分（供 writing-plans）

1. **前置：StateMachineBehaviour 隔离**（D7）
   - `AnimationCallbackRegistry` + 三个 behaviour 改造 + `FSMManager`/`Sys3CEntry` 注册/注销 + 双 Animator 回归。
2. **协议字段**
   - `EntitySnapshot` HP/MaxHP/IsDefending + mask 位 + `CastBroadcast` 落点/蓄力 + 往返测试。
3. **服务端快照**
   - `AoiBroadcaster` 填充 + `AoiBandScheduler` 变化检测 + Server.Tests。
4. **服务端战斗广播与名册**
   - `CastHandler` 收件人扩播/去重 + `CombatResolver` 填 CastBroadcast + `LoginHandler` 名册 + Server.Tests。
5. **`RemoteEntityAnimator`**
   - 快照/瞬时分离 + 仲裁器 + 参数/root motion/升档 + EditMode/PlayMode。
6. **客户端 vitals/名字/血条**
   - `RemoteEntityView`/`NetworkTargetable`/`PlayerNameRegistry` + `NameplateRenderer` 带血条模板 + label sink + registry 集成 + 测试。
7. **技能目录 + 远程施法 + 投射物对齐**
   - `SkillPresentationCatalog` + `RemoteCastPresenter` + Spawner 返回值/`ExplodeAt` + 假视觉测试 + 三客户端计数。
8. **目标锁定**
   - `NetworkTargetSelector` + `InputManager` `T`/UI 按钮 + `TargetPlayerId` 贯通 + 目标面板/选中环 + 测试。
9. **集成验收**
   - 三客户端 PlayMode、带宽测试、浏览器 E2E、模块 README 更新（SyncLayer/SyncCore/Nameplate/Protocol/Server）。

---

## 14. 明确不做（重复登记，防蔓延）

阵营/PvP 开关/计分/击杀播报；仇恨表/目标迟滞；唯一出生点；玩家碰撞；蓄力起手姿态；击退/浮空；掉落/经验/贡献；远程受击闪白/武器拖尾；bot 名牌/血条/目标锁定；协议版本提升；KCP 传输层改动。

---

## 15. 关键文件清单

| 层 | 文件 |
|---|---|
| 协议 | `Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs`、`Messages/Combat.cs` |
| 服务端 | `Server/Aoi/AoiBroadcaster.cs`、`Aoi/AoiBandScheduler.cs`、`Handler/CastHandler.cs`（含 `CombatResolver.Decide`）、`Handler/LoginHandler.cs` |
| 动画隔离 | `Sys3C/Animation/StateBehaviours/{Attack,Base,Hit}StateBehaviour.cs`、新增 `AnimationCallbackRegistry.cs`、`Sys3C/FSM/FSMManager.cs`、`Sys3C/Sys3CEntry.cs` |
| 远程表现 | `SyncLayer/RemoteEntityAnimator.cs`、`RemoteEntityRegistry.cs`、`ClientNetworkHost.cs`、`CombatNetworkBridge.cs`、`SkillCastReporter.cs` |
| 新增客户端 | `SyncLayer/{NetworkTargetable,PlayerNameRegistry,NetworkTargetSelector,RemoteCastPresenter}.cs` |
| 技能表现 | `Skills/Data/SkillPresentationCatalog.cs`、`Skills/Runtime/SwordQi/SwordQiSpawner.cs`、`Skills/Runtime/Projectile/FireballSpawner.cs`、`FireballProjectile.cs` |
| 名牌/血条 | `Nameplate/NameplateRenderer.cs`、`EntityDisplayManager.cs` |
| 输入/UI | `Sys3C/Input/InputManager.cs`、`MobileInputBinder.cs`、移动端按钮场景 |
| 资产 | `Assets/PreRes/SkillsCfg/`（7 个 SkillData）、`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`（名字/目录/按钮） |
| 测试 | `Assets/Scripts/Tests/Editor/...`、`Assets/Scripts/Tests/PlayMode/...`、`Server/Server.Tests/...` |

---

*说明：本规格把两次 Unity 运行时审核的结论作为硬约束：StateMachineBehaviour 按 Animator 隔离、快照状态与瞬时事件分离、远程攻击立即播放、远程投射物视觉对齐。实现计划由 `superpowers:writing-plans` 生成。*
