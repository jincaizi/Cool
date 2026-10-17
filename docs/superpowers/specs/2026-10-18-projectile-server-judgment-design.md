# 投射物类技能的服务端命中判定 设计

**Date:** 2026-10-18
**Engine:** Unity 2022.3.25f1
**Layer:** `AOT/Protocol`、`Hotfix/SyncCore`、`Hotfix/SyncLayer`、`Hotfix/Skills`、`Server/`
**状态:** 待实现

---

## 0. 结论摘要

联网模式下**剑气（以及火球/蓄力火球/陨石）打不中目标**。根因不是某一行写错，而是**投射物的命中判定从未服务端化**：

1. 本地伤害靠 `PhysicsRegistry`，而联网模式下怪物是**视图**、从不 `Init` ⇒ 注册表里没有怪物 ⇒ 本地弹体无目标；
2. 服务端只裁决「施法者位置 + 技能挥击形状」的一次性几何（命中帧延迟），**没有弹体/飞行/沿途判定**，协议里也没有落点或蓄力数据；
3. 设计文档明写「当前为本地实现，预留网络接入点」；`CombatRulesTests` 把 `IsSynced(20003/30001)==false` 钉死（决策 **D-P4-1**：投射物/蓄力/大招不进表）。

本设计**翻转 D-P4-1**（P4 计划 backlog 里已登记此项），用「服务端几何判定 + 客户端上报落点/蓄力进度」实现四个技能的联网命中。**客户端不再本地结算弹体伤害**，伤害权留在服务端（与既有 P4 模型一致）。

### 已拍板的决策

| 决策 | 选定 | 理由 |
|---|---|---|
| 判定模型 | **服务端线形/圆形几何判定**（A 方案） | 走现有 `HitResolver` 表驱动模式，确定性、可测；不引入服务端弹体实体 |
| 范围 | 10002 剑气 / 20003 火球 / 20004 蓄力火球 / 30001 陨石 | 四个"预留未接"的投射物类技能一次收口 |
| 落点 | **`CastRequest.ImpactPoint`**（客户端上报、服务端校验射程与有限性） | 客户端存在「纯位置施放」入口（`SkillToPosition`），只靠锁定目标 id 覆盖不全；锁定情形上报**施法时刻的目标位置**，比服务端读实时位置更忠实 |
| 蓄力进度 | **`CastRequest.ChargeProgress`**（钳 [0,1]），**在释放时上报** | 蓄力半径/伤害随进度缩放；`OnSkillActivated` 在蓄力**开始**就触发，释放路径无事件 ⇒ 必须新增释放发送点 |
| 剑气分段 | 飞行按 **3 段**线形判定（沿途全部、每目标一次） | 保留"途经全中"语义；固定 tick 服务端也能表达飞行 |
| 火球语义 | **线形找首个候选 → 以该点为中心 1.5m AOE**；首段命中即停止后续段 | 与客户端"首目标爆炸 + 溅射"一致，而不是"沿线全打" |
| 蓄力/陨石 | **单次圆形判定**：蓄力在释放帧（中心 = 落点）、陨石在 0.8s 落地时（中心 = 落点） | 两者的客户端语义本就是"点爆" |
| 服务端伤害 | 表值 = 客户端资产 `_baseDamage` 与 `_criticalRateBonus`（**已逐条实证**） | `SkillOwnerProxy.Stats == null` ⇒ `CalculateFinalDamage` 不叠加属性；现有 4 条表项与资产逐条相等 |

---

## 1. 证据（根因）

| # | 事实 | 出处 |
|---|---|---|
| 1 | 剑气伤害走 `PhysicsRegistry.FindNearby(..., EntityType.Monster)` | `SwordQi/SwordQiProjectile.cs:58-72` |
| 2 | 只有 `MonsterEntity.Init` 会 `Register`；联网怪物是视图、从不 `Init` | `MonsterEntity.cs:69-80`；2026-10-17 npc 视图 NRE 的同一事实 |
| 3 | 服务端只做一次性几何：`HitResolver.Resolve(casterPos, yaw, targetId, shape, candidates)` | `Server/Handler/CastHandler.cs:110-140`、`SyncCore/HitResolver.cs:35-100` |
| 4 | 表内只有 4 个玩家技能；`IsSynced(20003/30001)==false` 被测试钉死 | `SyncCore/CombatRules.cs:77-90`、`Tests/Editor/SyncCore/CombatRulesTests.cs:16-17`（服务端镜像同） |
| 5 | D-P4-1：「投射物（Fireball/ChargeBlast 蓄力/Ultimate）不进表……理由：飞行/蓄力/引导的服务端化复杂度高」 | `docs/superpowers/plans/2026-09-11-p4-skill-combat-sync.md:33` |
| 6 | 剑气设计文档：「当前为本地实现，预留网络接入点」「`SwordQiSpawnedEvent` 本次无订阅者」 | `docs/superpowers/specs/2026-08-07-sword-qi-attack2-design.md:8,21,146` |
| 7 | 协议无客户端→服务端的命中通道；战斗只有 `CastRequest/CastBroadcast/DamageResult/RespawnNotice` | `AOT/Protocol/MessageId.cs:35-49` |
| 8 | 火球：抛物线 + 首目标 0.6m 接触爆炸 + **1.5m 溅射** | `Projectile/FireballProjectile.cs:60-140` |
| 9 | 陨石：`WarnDuration=0.8s` 后以 `_impactPoint` 为中心 `ExplosionRadius=4` 点爆 | `Meteor/MeteorStrike.cs:44-75`、`MeteorConfig_Fire.asset:16` |
| 10 | 蓄力：释放点 AOE，中心 = **锁定目标位置**（有锁）否则施法者；半径与伤害随蓄力缩放，资产曲线**均为 1→1.5** | `SkillExecutor.cs:640-650`、`ChargeFire_SkillR.asset:53-100` |
| 11 | `OnSkillActivated` 在**蓄力开始**触发；释放走 `TryReleaseCharge → Release()`，**无事件** | `SkillCoordinator.cs:92-98, 289` |
| 12 | 资产 `_baseDamage` 与表值逐条相等（50/90/120/160），crit 也相等（0.15/0.15/0.2/0.25）；`SkillOwnerProxy.Stats == null` | `Assets/PreRes/SkillsCfg/*.asset`、`Sys3CEntry.cs:832` |
| 13 | `ProjectileSkillData` **没有 ShapeBlock**（源码注释：projectile handles its own collision detection） | `ProjectileSkillData.cs:64` |
| 14 | 剑气生成偏移 `(0.8, 1.0, 0.6)` 是角色局部坐标（前向 = z=0.6）；射程 12、判定半径 2、速度 18 | `SwordQi_Heavy.asset:15-20` |
| 15 | Instant/Combo/Projectile 跳过 Cast 直接进 Execution（激活当帧生成弹体） | `SkillStateMachine.cs:107-111` |

---

## 2. 协议改动（两处，最小）

`AOT/Protocol/Messages/Combat.cs` 的 `CastRequest`：

```csharp
[ProtoMember(4)] public float ImpactX { get; set; }   // 落点（20004/30001 必填；其余忽略）
[ProtoMember(5)] public float ImpactY { get; set; }
[ProtoMember(6)] public float ImpactZ { get; set; }
[ProtoMember(7)] public float ChargeProgress { get; set; }  // 20004 用；服务端钳 [0,1]
```

**服务端校验（`CombatRules.ValidateProjectileRequest`，新）**：

- `ImpactX/Y/Z` 必须 `float.IsFinite`；距施法者 **XZ** 距离 ≤ 该技能 `ProjectileSpec.MaxImpactDistance`（表值，见 §3）；
- `ChargeProgress` 钳到 `[0,1]`（NaN 视为 0）；
- **只有 `Kind == CircleAtPoint` 的技能**（20004/30001）校验落点；`LineAll`/`LineNearestSplash`（10002/20003）忽略这两个字段（保持既有行为）。

**`HitShape` 不改**：线形/圆形的宽度与半径由服务端 `ProjectileSpec` 自持构造 ⇒ 不新增任何可篡改字段。`ValidateShape` 保持原样（`ProjectileSkillData` 发零形状，命中"无挥击"表项的最小上限）。

---

## 3. 服务端判定

### 3.1 数据结构

`SyncCore/CombatRules.cs`：

```csharp
public enum ProjectileKind { LineAll, LineNearestSplash, CircleAtPoint }

public sealed class ProjectileSpec
{
    public ProjectileKind Kind { get; }
    public float Length { get; }          // 线长（LineAll/LineNearestSplash）
    public float HalfWidth { get; }       // 线半宽
    public float Radius { get; }          // 爆心半径（LineNearestSplash 的溅射 / CircleAtPoint 的 AOE）
    public int Stages { get; }            // 分段数（LineAll/LineNearestSplash；1 = 单次）
    public float FirstDelaySeconds { get; }  // 首个判定距施法决策的秒数
    public float TotalFlightSeconds { get; } // 首段到末段的总时长（Stages>1 时有效）
    public float MaxImpactDistance { get; }  // 落点距施法者的上限（带点类技能用）
    public bool ChargeScales { get; }        // 20004：半径/伤害随 ChargeProgress 缩放
    public float ChargeAreaMaxScale { get; } // 1.5（资产曲线末值）
    public float ChargeDamageMaxScale { get; } // 1.5（资产曲线末值）
}
```

`SkillEntry` 增加可选 `ProjectileSpec Projectile`；表新增/修改（**形状上限必须容纳客户端真实发来的形状，否则每次施法都被判 `shape-tampered`**）：

| SkillId | ShapeType | MaxRange | MaxAreaRadius | MaxAngle | Cooldown | BaseDamage | CritRate | HitDelay | ProjectileSpec |
|---|---|---|---|---|---|---|---|---|---|
| 10002 | 0 | 2.5（不变） | 0 | 120（不变） | 0.5（不变） | 90（不变） | 0.15（不变） | 0.35（不变） | `LineAll{Length=12, HalfWidth=2, Stages=3, FirstDelay=0.35, Flight=0.667}` |
| 20003 | 0 | **0** | 0 | **0** | **3**（资产 `_cooldown: 3`） | **20** | **0** | 0 | `LineNearestSplash{Length=15, HalfWidth=0.6, Radius=1.5, Stages=3, FirstDelay=0, Flight=0.833}` |
| 20004 | **1**（资产 `_targetType: 1`） | **2**（资产 `_range: 2`） | **2**（资产 `_areaRadius: 2`） | **120** | **6**（资产 `_cooldown: 6`） | **40** | **0** | 0（释放帧；资产 `_hitboxTimings: [0]`） | `CircleAtPoint{Radius=2, MaxImpactDistance=30, ChargeScales=true, AreaMax=1.5, DamageMax=1.5}` |
| 30001 | 0（资产 `_targetType: 0`） | **2**（资产 `_range: 2`） | 0 | **120** | **25**（资产 `_cooldown: 25`） | **80** | **0** | **0.8**（= `WarnDuration`；资产 `_hitboxTimings: []`，无挥击判定） | `CircleAtPoint{Radius=4, MaxImpactDistance=30}` |

说明：
- 20003 的客户端形状是**全零**（`ProjectileSkillData` 无 ShapeBlock）⇒ 上限全 0，校验通过且不产生挥击判定。
- 20004/30001 有真实 ShapeBlock（`IStatus`：`AOE_Circle` / `Single`），客户端会把它们发上来 ⇒ 上限按资产值填。
- `MaxImpactDistance = 30` 是**防篡改上限**，不是技能射程；客户端对"锁定远处目标"的施放合法（`DetectAOETargets` 以目标为中心、不限距离），填小了会把合法施法拒成 `impact-too-far`。

### 3.2 判定与调度

- `PendingCast` 扩展为**判定组**：`(casterId, request, shape, kind, stages, firstTick, tickStep, groupId)`；`DrainResolved` 在到期 tick 逐段结算。
- **首段 tick 捕获起点**（剑气）：`LineAll` 的起点在**首个到期 tick** 读施法者位置 + yaw，之后各段复用（剑气不随施法者移动）。`LineNearestSplash`（火球）同理，捕获点 = 首段 tick 的施法者位置。
- **LineAll**（剑气）：第 k 段 = 从 `origin + forward*(k*Length/Stages)` 起、长 `Length/Stages`、半宽 `HalfWidth` 的带内候选；每目标只结算一次（组内 dedup 集）。
- **LineNearestSplash**（火球）：第 k 段在该段内找**最近**候选；找到 ⇒ 以**该候选位置**为中心、`Radius` 做圆判定；**同组后续段作废**（一次爆炸）。
- **CircleAtPoint**：中心 = `ImpactPoint`（20004 若客户端报了锁定目标位置也走这里）；20004 的半径与伤害按 `ChargeProgress` 线性插值到 `ChargeAreaMaxScale/DamageMaxScale`。
- **伤害**：沿用 `RollDamage(entry)`（`BaseDamage × crit`）；20004 再乘蓄力伤害倍率。**剑气与挥击是两个独立伤害实例**（与单机一致，不跨组去重）。

### 3.3 时序表（客户端表现 ↔ 服务端判定）

| 技能 | 客户端表现 | 服务端判定 |
|---|---|---|
| 10002 | 0.35s 挥击帧发射剑气，速度 18、射程 12（0.667s 飞完） | 0.35s 挥击 + 0.35→1.017s 三段线形 |
| 20003 | 激活当帧发射火球（抛物线） | 0→0.833s 三段「线找首个 → 1.5m 圆」 |
| 20004 | 释放帧以目标/自身为中心爆 AOE（半径 2→3、伤害 ×1→1.5） | 释放帧一次圆判定（半径与伤害按进度缩放） |
| 30001 | 落点预警 0.8s 后 4m AOE | 0.8s 一次圆判定 |

---

## 4. 客户端改动

| 位置 | 改动 |
|---|---|
| `HitShapeConverter.GetShape` | 无需新增分支（`ProjectileSkillData` 无 Shape，发零形状；表项上限为 0 ⇒ 校验通过且无挥击判定）。**加一条注释说明这是刻意的** |
| `CombatNetworkBridge.NotifySkillCast` | 增加可选参数 `impactPoint`、`chargeProgress`；写入 `CastRequest` |
| `CombatNetworkBridge` 接线 | **非蓄力技能**：沿用 `OnSkillActivated` 发送。其中 30001 带 `ImpactPoint = _targetPosition`（客户端 `SpawnMeteor` 对零值兜底为"施法者前方 12m"，接线处照抄同一兜底）；10002/20003 **不使用** `ImpactPoint`（剑气/火球都沿施法者朝向）。**蓄力技能（20004）**：激活时**不发**，在释放时发送，`ImpactPoint` = 释放时刻的目标位置（无锁定则为施法者位置，与 `DetectAOETargets` 的中心一致）+ `ChargeProgress` |
| `SkillCoordinator` | 新增 `event Action<SkillData, float, Vector3> OnChargeReleased`（`skillData, chargeProgress, impactPoint`），在 `TryReleaseCharge` 成功后触发；`impactPoint = _targetCharacter?.transform.position ?? 本地施法者位置`（与 `DetectAOETargets` 的中心一致）。取消蓄力不触发、不发请求、不占冷却 |
| `Sys3CEntry` | 把 `OnChargeReleased` 转给 `CombatNetworkBridge`（与既有 `OnSkillActivated` 接线同处） |
| `SwordQiProjectile` / `FireballProjectile` / `MeteorStrike` | **联网时跳过本地伤害**（`NetworkModeService.Load() == Online` 直接 return，保留表现）；单机行为逐字不变 |
| `CombatRulesTests`（客户端 + `Server/Server.Tests` 两处） | 翻转 `IsSynced(20003/30001)` 断言；新增 20004 |

---

## 5. 伤害对齐口径（实证）

- `DamageBlock.CalculateFinalDamage(stats)`：`damage = _baseDamage + stats.GetAttribute(_scalingAttribute) * _attackRatio`。
- 本地玩家的 `SkillOwnerProxy.Stats == null` ⇒ 属性项恒为 0 ⇒ **最终伤害 = `_baseDamage`**（`_attackRatio` 对玩家无效）。
- 现有 4 条表项与资产逐条相等：`BA_Combo1 50 / BA_Heavy 90 / Instant_SkillQ 120 / Spin_SkillR 160`，crit `0.15/0.15/0.2/0.25`。
- 新表项：20003 = 20、20004 = 40、30001 = 80，crit 全 0（资产如此）⇒ **单机与联机数值一致**。
- **边界**：若将来给玩家接上 `IEffectStats`，属性项不再为 0，服务端表值必须重新标定（届时以「基准属性下的最终伤害」入表）。

---

## 6. 测试计划（红-绿，全部可自动）

| 层 | 用例（新增/修改） | 断言要点 |
|---|---|---|
| SyncCore `HitResolverTests` | `test_line_hits_all_along_the_band_once` | 带内多目标全命中、带外/背后不中、逐段 dedup |
| SyncCore `HitResolverTests` | `test_line_nearest_picks_nearest_and_stops` | 选段内最近者，且组内后续段不再结算 |
| SyncCore `CombatRulesTests` | 翻转 `IsSynced(20003/30001)`、新增 20004；`ValidateProjectileRequest` 的 finite/距上限/蓄力钳制 | 表与校验规则 |
| Server `CombatResolverTests` | 10002 剑气：0.35s 后三段线形，命中 12m 内直线目标；挥击锥外、剑气线内的目标**能**被命中（今天为红） | 分段调度 + 起点捕获 |
| Server `CombatResolverTests` | 20003：首目标处 1.5m 溅射，后续段作废 | 火球语义 |
| Server `CombatResolverTests` | 20004：释放请求带 `ChargeProgress`，半径/伤害按 1→1.5 缩放；蓄力取消不发请求 | 释放发送点 + 缩放 |
| Server `CombatResolverTests` | 30001：落点圆 4m、0.8s 后结算；落点距超上限被拒 | 落点校验 |
| Client EditMode `CombatNetworkBridgeTests` | `NotifySkillCast` 写入 ImpactPoint/ChargeProgress；蓄力技能激活不发、释放才发 | 发送点 |
| Client EditMode `NetworkModeDamageGuardTests`（新） | 三个弹体在 Online 下不调本地 `TakeDamage`，Offline 下照旧 | 本地伤害守卫 |
| PlayMode（可选，若有服务器） | `CombatSyncTests` 增一条"剑气命中远端怪" | 端到端 |

**变异检查**（至少一处，证明断言有牙齿）：把 10002 的 `ProjectileSpec.Length` 改 0 → "剑气线内命中"用例必须变红。

---

## 7. 明确不做

- 弹体给**其他客户端**的表现同步（`SwordQiSpawnedEvent`/`FireballSpawnedEvent`/`MeteorSpawnedEvent` 仍无订阅者；观察者看不到弹体）。
- 怪物视图的**受击动画**（`DamageResult` 已能让它飘字）。
- 引导类/其它"预留"技能。
- 服务端模拟重力/落地/地形遮挡（见 §8）。
- 蓄力过程的**服务端计时**（只在上报释放时带进度）。

---

## 8. 已知边界与风险（诚实登记）

| 边界 | 说明 |
|---|---|
| 服务端无地形/遮挡 | 线形与圆形判定**穿墙**、不在首个目标处停下（火球除外——它按"首个候选"停）。现有锥形判定同样无遮挡检查，属同一哲学 |
| 火球不模拟重力与落地 | 服务端按水平前向线找首个目标；客户端会因重力提前落地。近距离目标一致，远距离/高台会有偏差 |
| 时序误差 | 火球三段每段 ≈0.28s（15Hz tick 下 4 tick），接触时刻最多晚 0.28s；剑气同理每段 ≈0.22s |
| 落点信任面 | `ImpactPoint` 由客户端上报，服务端只校验**距离上限与有限性**——与「客户端权威位置 + 服务端部分校验」同一哲学，不是反作弊闸门 |
| 蓄力进度信任面 | `ChargeProgress` 只钳 [0,1]；满蓄力可被伪造，但半径/伤害上限由表封顶 |
| 表值与资产耦合 | 冷却/伤害若在资产侧改动，服务端表需同步（本次新增 3 条表项；已有 4 条同理） |
| 单机与联机的火球差异 | 单机是抛物线弹道 + 落地爆炸；联机是水平线 + 首目标爆炸（见上） |
