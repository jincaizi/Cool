# 剑气格挡与举盾精力统合设计

**Date:** 2026-10-21
**Engine:** Unity 2022.3.25f1
**Layer:** `Server`、`Hotfix/SyncLayer`、`Hotfix/Sys3C`、`Hotfix/Skills`
**状态:** 设计已批准，待实现
**取代:** `docs/superpowers/specs/2026-10-18-stamina-system-design.md` 第 16 条
“与盾耐久解耦”——本设计删除盾耐久，盾只受精力控制。

---

## 0. 结论摘要

1. 剑气（`LineAll`，当前只有 10002）遇到**格挡成功**的玩家时被吸收：该目标按现有格挡规则受伤，
   同段更远的目标与后续所有段不再命中；未举盾/背对/怪物照旧被穿透。
2. 删除服务端盾耐久与客户端单机盾耐久。盾的寿命只由精力决定：持盾 10/s + **每次成功格挡 15**。
3. 联机客户端在收到服务端的停点结果后，让剑气**飞到停点再消失**；停点由 `DamageResult`
   新增的三个字段下发。
4. 挥击与剑气仍是两个独立伤害实例；近距离一次 10002 同时打出两者且都被格挡时，共扣 30 精力。
5. 已知限制：服务端不再有任何盾资源兜底；改客户端可无限举盾（纯客户端精力权威的直接后果）。

### 已拍板的决策

| 决策 | 选定 | 理由 |
|---|---|---|
| 剑气阻挡条件 | **格挡成功**才吸收 | 用户原话“举盾的玩家不能穿透”；背对/未举盾仍穿透（耐久已删除） |
| 阻挡范围 | 所有 `ProjectileKind.LineAll` | 当前只有 10002；未来 LineAll 自动继承 |
| 段内结算顺序 | 按沿飞行方向距离升序，同距离按 playerId | 确定性；第一个格挡者吸收，更近目标先结算 |
| 剑气格挡几何 | 线形命中用 `pending.Origin`；挥击用施法者当前位置 | 剑气飞行 0.4–0.9s，施法者可能移动；否则正面格挡误判 |
| 盾资源 | **删除盾耐久，只保留精力** | 用户明确要求“盾只受精力控制” |
| 格挡精力成本 | **15/次**，固定值 | 用户选定；与举盾 10/s 叠加 |
| 近距离双实例 | 挥击+剑气都挡 = 30 | 两个独立伤害实例，按“每次成功格挡”各扣一次 |
| 精力扣减判定 | 以服务端 `IsBlocked` 为准，眩晕也扣 | 本地不再看是否还举着盾；否则松手一帧可免精力格挡 |
| 精力不足 | 扣到 0、力竭、强制收盾+锁存 | 本次格挡已被服务端裁决，撤不回 |
| 格挡伤害 | 仍按 `BlockDamageScale = 0.3` | 吸收剑气不等于免伤 |
| 视觉消失 | 结果驱动 + 飞到停点再消失 | 不做远端举盾状态同步；优化“半空提前消失” |
| 停点下发 | `DamageResult` 新增 `ProjectileStopped/StopX/StopZ` | 结果包无法区分挥击格挡与剑气格挡；客户端扫描 `IsBlocked` 会误停 |
| 协议版本 | **不升** `ProtocolVersion` | 只加不删的字段扩展，与 `IsDefending`/`Dead`/`IsBlocked` 同惯例 |
| 死亡/复活 | 幂等退出防御 | 盾只受精力控制后，这是唯一可靠的状态清理点（纳入本次） |

---

## 1. 现状与证据

| # | 事实 | 出处 |
|---|---|---|
| 1 | 10002 = `LineAll{Length=12, HalfWidth=2, Stages=3, Flight=0.667s, KeepsSwing=true}`，伤害 90、暴击 15% | `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs:128` |
| 2 | 服务端按段结算：`ResolveLineAll` 返回候选遍历顺序，`ApplyHit` 对每个目标做格挡/扣血 | `Server/Handler/CastHandler.cs:150-263` |
| 3 | 现有格挡条件：`IsDefending && ShieldDurability > 0 && 正面 60° 锥`；格挡伤害 ×0.3、每击扣 25 耐久 | `Server/Handler/CastHandler.cs:241-250` |
| 4 | `IsDefending` 由客户端经 `PositionSyncRequest`（tag 8）上报，服务端只做几何/耐久校验 | `Server/Handler/PositionSyncHandler.cs:50`、`PlayerState.cs:31-35` |
| 5 | 客户端剑气在联网模式下跳过本地伤害，只保留表现 | `Skills/Runtime/SwordQi/SwordQiProjectile.cs:59-75` |
| 6 | 本地精力纯客户端权威；格挡结果经 `ApplyServerDamage(..., isBlocked)` 到达本地 | `Sys3C/Sys3CEntry.cs:577-631`、`SyncLayer/CombatNetworkBridge.cs:98-137` |
| 7 | 远端玩家的举盾状态不在 AOI 快照；施法者客户端无法提前知道谁在举盾 | `Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs:11-26`、`Server/Aoi/AoiBroadcaster.cs:58-74` |
| 8 | 单机盾耐久在 `CharacterController`（上限 50），盾破走 `HandleShieldBreak` 1.5s 眩晕 | `Sys3C/Character/CharacterController.cs:44-56,529-568`、`FSM/FSMManager.cs:157-167` |
| 9 | `DamageResult` 现有 tag 1–4，`HitEntry` tag 1–7；协议只加不删 | `Assets/Scripts/AOT/Protocol/Messages/Combat.cs:61-93` |
| 10 | 既有服务端格挡测试：四击后第 5 击不再减伤（耐久耗尽） | `Server/Server.Tests/Combat/BlockMitigationTests.cs:116-136` |
| 11 | 既有 `BlockMitigationE2ETests` 用 10001 在 B 位 `(0,1.5)` 做一次格挡验收 | `Assets/Scripts/Tests/PlayMode/BlockMitigationE2ETests.cs:34-125` |
| 12 | `ShieldSystemAdapter` 是属性护盾 buff，与举盾耐久无关 | `Sys3C/Character/CharacterAdapters.cs:154` |

---

## 2. 目标与非目标

### 目标

1. 剑气在第一个格挡成功的玩家处被吸收，不再穿透到其身后。
2. 删除盾耐久；盾的持续时间只由精力决定（10/s + 15/次格挡）。
3. 联机施法者客户端在停点处结束剑气特效。
4. 单机/联机格挡都扣 15 精力；力竭强制收盾；复活回满。
5. 用服务端/客户端/联机 E2E 测试钉死上述语义与数值。

### 非目标

- 远端举盾状态同步（AOI 字段）与远端举盾/格挡动画。
- 服务端权威精力或服务端盾资源。
- 火球/陨石/蓄力爆的格挡几何改动（保持现状）。
- 每次施法最多扣一次精力的上限（用户确认按两个实例各扣）。
- 盾破动画/眩晕的保留或重做（随耐久一起删除）。
- 修改离线 `DefendModifier` 的 80% 减伤/160° 与联机 0.3/120° 的既有差异。
- 精力环 UI 改动（复用现有 `StaminaRingRenderer`）。

---

## 3. 规则

### 3.1 盾资源模型

- 精力上限 100；持盾 10/s；每次成功格挡 **15**；回复 33.333/s（未力竭延迟 0.2s、力竭 0.75s）。
- **不存在盾耐久**：服务端 `PlayerState.ShieldDurability`、`CombatRules.ShieldMaxDurability`、
  `ShieldDurabilityPerBlock`、客户端 `CharacterController._shieldDurability` 及盾破分支全部删除。
- 精力归零 ⇒ 力竭 ⇒ `ForceExitDefendByExhaustion()`：强制收盾、置 `_defendStaminaLatch`；
  必须松开防御键并在回复到 20% 后才能重新举盾。
- 本次格挡已由服务端裁决，扣精失败不会撤销减伤；只影响后续攻击。
- 单机盾不再会“被打碎”；连续格挡直到精力耗尽。
- **不动 `ShieldSystemAdapter`**（属性护盾/伤害吸收 buff，与举盾是两套东西）。

### 3.2 剑气阻挡

适用范围：`ProjectileKind.LineAll`（当前只有 10002）。

服务端每段结算：

1. 候选 = 现有 `BuildCandidates`（玩家施法：玩家/bot + 怪物；怪物施法：已登录玩家；bot 不举盾）。
2. 计算该段的 `hitIds` 后，按**沿 `pending.Yaw` 距 `pending.Origin` 的距离升序**排序；
   同距离按 playerId 升序，保证确定性。
3. 依次结算；对每个目标：
   - 格挡成功条件：`target.IsAlive && target.IsDefending && IsBlocked(target.Rotation, attackerPos - target.Position)`；
     `attackerPos` 在**线形命中**时 = `pending.Origin`。
   - 格挡成功 ⇒ 伤害 × `BlockDamageScale`（0.3），标记该 `HitEntry.IsBlocked = true`；
     该目标是**吸收点**：同段更远目标不再结算，`pending.Spent = true`，后续所有段作废；
     本次 `CastResolution` 记录 `ProjectileStopped = true`、`StopPosition = target.Position`。
   - 未格挡成功 ⇒ 正常伤害，继续下一个目标。
4. 未举盾、背对（锥外）、死亡目标、所有怪物：照旧被穿透。
5. 10002 的近身挥击实例独立结算（`KeepsSwing`）：
   - 挥击循环使用**施法者当前位置**作为格挡几何的 `attackerPos`；
   - 挥击格挡**不**吸收剑气、不置 `ProjectileStopped`；
   - 挥击先结算，随后才是线形命中。
6. 火球（`LineNearestSplash`）与蓄力/陨石（`CircleAtPoint`）保持现有格挡几何与“首中即停/点爆”语义，
   本设计不改。

**边界语义（写死）**：

- 格挡者必须处于**正面 60° 半角锥**内（`CombatRules.BlockHalfAngleDeg`）；背对不算格挡，剑气继续穿透。
- 死亡目标不吸收剑气：同一段里挥击可能先杀死目标，线形命中对其只造成伤害、不格挡、不停。
- 同距离多目标按 playerId 排序；先到者若是格挡者则吸收，后到者不再结算。
- 吸收只发生在**线形命中**；挥击格挡不影响剑气是否继续飞。

### 3.3 格挡精力成本

- 每次成功格挡额外扣 **15** 点精力（`TryConsume(15)`），与持盾 10/s 叠加。
- 触发点：
  - 联机：`DamageResult` 中本地玩家命中 `IsBlocked = true` → `Sys3CEntry.ApplyServerDamage` 的格挡分支；
  - 单机：`Sys3CEntry.IDamageable.TakeDamage` 中 `_cc.IsDefending && wasBlocked` 分支。
- **以服务端 `IsBlocked` 为准**：客户端收到结果时不再检查 `_cc.IsDefending`。
  否则玩家在结果到达前一帧松手即可获得免精力格挡。
- **眩晕也扣**：15 点是离散事件成本，不受“眩晕期间不扣每秒举盾消耗”的豁免。
- 同一 `DamageResult` 中挥击与剑气都被格挡 ⇒ 调用两次 `ApplyServerDamage` ⇒ 共扣 30。
- 精力不足 15：`TryConsume` 扣到 0、置力竭、`ForceExitDefendByExhaustion()`；本次格挡照常减伤。
- 扣精必须在 `EvaluateLocalDeath()` **之前**；致死格挡也要先扣。
- 复活 `Refill()` 回满并清除力竭；不退还已扣精力。

### 3.4 剑气视觉（联机、结果驱动）

- `DamageResult` 新增三个线上字段：

  | tag | 字段 | 语义 |
  |---|---|---|
  | 5 | `ProjectileStopped` (bool) | 本次结果包含 LineAll 线形吸收 |
  | 6 | `StopX` (float) | 吸收点世界 X |
  | 7 | `StopZ` (float) | 吸收点世界 Z |

- 服务端只在 `LineAll` 线形命中格挡并置 `Spent` 时填；挥击格挡不填。
- 客户端 `CombatNetworkBridge` 在 `DamageResult` 处理完后触发一次
  `OnLineProjectileStopped(Vector3 point)`，条件：
  `ProjectileStopped && SourcePlayerId == 本地玩家 && 该技能 Projectile.Kind == LineAll`。
- `ClientNetworkHost` 用**命名方法**订阅，在 `DetachCombatBridge` 退订。
- `SwordQiSpawner` 维护活跃剑气列表，`StopNearest(point)` 选择：
  - 横向距离最小者优先；
  - 已飞过停点（`along < traveled - 1.5m`）加 1000 惩罚；
  - 存在未飞过候选时：横向相同按飞行距离更短（更新的一发）优先；
  - **全部候选都已飞过**时：选 `traveled - along` 最小（刚飞过最少）的一发并立即释放；
  - 没有活跃剑气 ⇒ no-op（**不做** pending 队列，避免停掉下一次施法）。
- `SwordQiProjectile.StopAtPoint(point)`：
  - 把 `point - spawn` 与 `direction` 压到 XZ 后投影得到沿程距离；
  - `_stopDistance = Clamp(along, _traveled, Range)`；
  - `Tick` 中 `_traveled >= _stopDistance` ⇒ `Release()`；
  - `OnSpawn` 重置 `_spawnPosition`、`_stopDistance = Range`。
- 残余误差：客户端剑气生成点有 `(0.8, 1.0, 0.6)` 局部偏移，停点可能差 **≤1m**；这是结果驱动方案的固有误差。
- 远端施法者的剑气在本地没有特效，不做处理。

### 3.5 死亡/复活防御清理（纳入本次）

- `EvaluateLocalDeath()`、`ApplyServerRespawn()`、`TickRespawn()` 三处各执行一次幂等的
  `_cc.TryExitDefend(); _fsmManager.ExitDefend();`。
- 目的：盾只受精力控制后，死亡/复活的 `IsDefending` 残留没有其它清理路径；
  服务端 `IsAlive` 已挡住死者格挡，但复活首帧的上报窗口仍会误判。

---

## 4. 架构与组件

### 服务端

| 文件 | 改动 |
|---|---|
| `Server/Player/PlayerState.cs` | 删除 `ShieldDurability`、`ConsumeShieldDurability`、`TryRespawn` 回满盾；更新 `IsDefending` 注释；删除不再使用的 `using Cool.SyncCore;` |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs`（双端共享） | 删除 `ShieldMaxDurability`、`ShieldDurabilityPerBlock`；更新格挡注释为“盾资源归客户端精力，服务端无耐久” |
| `Server/Handler/CastHandler.cs` | `ApplyHit` 增加 `Vec3 attackerPos` 参数并返回 `bool blocked`；格挡条件去掉耐久、加 `target.IsAlive`；去掉耐久扣除；`ResolveStage` 对 `LineAll` 按距离排序、传 `pending.Origin`、遇到 blocked 置 `Spent`/停点字段并 break；`CastResolution` 增加 `ProjectileStopped`/`StopPosition`；`BroadcastDamageAsync` 拷贝到 `DamageResult` |

### 协议

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/AOT/Protocol/Messages/Combat.cs` | `DamageResult` 追加 tag 5/6/7（`ProjectileStopped`/`StopX`/`StopZ`），注释说明“只加不删、不升 ProtocolVersion” |

### 客户端

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs` | 新增 `event Action<Vector3> OnLineProjectileStopped`；`ApplyDamageResult` 末尾按“来源+技能类型+停点”触发一次 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` | 命名方法订阅/退订；转调 `SwordQiSpawner.StopNearest`；更新 `PlayerState.ShieldDurability` 相关注释 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiProjectile.cs` | `_spawnPosition`/`_stopDistance`/`OnSpawn` 重置；`IsActive`/`StopDistance`/`Traveled` 只读面；`StopAtPoint`；`Tick` 停点释放 |
| `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQi/SwordQiSpawner.cs` | `_active` 列表、`Spawn` 加入/清理、`StopNearest`、`DisposeForTests` 清空 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | `_blockStaminaCost = 15f`；`ConsumeBlockStamina()`；联机/单机格挡分支调用；删除单机盾破整段；死亡/复活退出防御；更新耐久注释 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs` | 删除 `_shieldDurability`、`MaxShieldDurability`、`ShieldDurabilityPercent`、`IsShieldBroken`、`AbsorbDamage`、`OnShieldBreak`、`TryEnterDefend` 重置 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs` | 删除 `HandleShieldBreak`；更新第 118 行引用它的注释 |

### 数据流

```
服务端线形段
  → hitIds 按 pending.Origin 距离升序
  → ApplyHit(attackerPos = pending.Origin)
      ├─ blocked=false → 正常伤害，继续
      └─ blocked=true  → 伤害 ×0.3
                       → CastResolution.ProjectileStopped=true / StopPosition=target.Position
                       → pending.Spent=true, break（后续段作废）
  → BroadcastDamageAsync → DamageResult(ProjectileStopped/StopX/StopZ)

目标客户端（本地玩家被挡）
  → CombatNetworkBridge → ApplyServerDamage(blocked=true)
  → ConsumeBlockStamina：精力 −15 → 不足则 0/力竭/强制收盾
  → EvaluateLocalDeath（扣精之后）

施法者客户端（本地玩家施法）
  → CombatNetworkBridge.OnLineProjectileStopped(stopPoint)
  → SwordQiSpawner.StopNearest(stopPoint)：横向最近、未飞过、优先新发
  → SwordQiProjectile.StopAtPoint → 飞到停点 → Release

单机格挡
  → IDamageable.TakeDamage → DefendModifier 判正面
  → wasBlocked=true → ConsumeBlockStamina（15）
```

---

## 5. 协议与兼容性

- `DamageResult` 现有 tag 1–4（`SourcePlayerId`/`SkillId`/`ServerTimestamp`/`Hits`），
  追加 tag 5/6/7。protobuf-net 对未知字段/缺省字段天然兼容。
- **不升 `ProtocolConstants.ProtocolVersion`**：沿用 `IsDefending`（tag 8）、`Dead`（tag 12）、
  `IsBlocked`（tag 7）的“只加不删”惯例；升版本会让旧客户端/服务端直接握手失败。
- 旧服务端 → 新客户端：三个字段恒 false/0，剑气特效照旧飞完（玩法由服务端决定）。
- 新服务端 → 旧客户端：旧客户端忽略未知字段，不影响伤害/格挡。
- `ProtocolShapeGuardTests` 增加 tag 钉死；`CombatMessagesTests` 增加往返断言。

---

## 6. 边界与不变量

| # | 不变量 |
|---|---|
| 1 | 只有 `LineAll` 线形命中会吸收剑气；挥击格挡不会 |
| 2 | 吸收点是第一个**格挡成功**的目标；更近的目标已先结算，更远/后续段不再结算 |
| 3 | 格挡成功 = 存活 + 举盾 + 攻击者在正面 60° 半角锥内；**无耐久条件** |
| 4 | 线形格挡几何用 `pending.Origin`；挥击用施法者当前位置 |
| 5 | 死亡目标不格挡、不吸收 |
| 6 | 格挡伤害仍 ×0.3，不是 0 |
| 7 | 每次成功格挡扣 15；同一施法挥击+剑气都挡 = 30 |
| 8 | 扣精以服务端 `IsBlocked` 为准；眩晕也扣；扣精先于死亡回调 |
| 9 | 精力不足 ⇒ 0 + 力竭 + 强制收盾 + 锁存；本次减伤不撤销 |
| 10 | 剑气视觉停点由服务端结果驱动；没有活跃剑气时 no-op |
| 11 | 剑气停点残余误差 ≤1m；重叠两发时按“横向最近、未飞过、优先新发”选择 |
| 12 | 服务端没有盾资源；盾的寿命完全由客户端精力决定（信任面登记） |
| 13 | 删除耐久后，单机/联机都不再有“盾破”事件或眩晕 |

---

## 7. 测试与验收

### 7.1 服务端（xUnit）

**新文件 `Server/Server.Tests/Handler/CombatResolverSwordQiBlockTests.cs`**（独立文件，避免把
`CombatResolverProjectileTests` 撑大；自带小型 `World` 助手，风格同 `BlockMitigationTests`）。

| 用例 | 关键装置与断言 |
|---|---|
| `SwordQi_Stops_At_Blocking_Player_And_Skips_Targets_Behind` | 施法者 p1 (0,0) yaw0；防守者 p3 (0,5) yaw180 `IsDefending`；NPC p2 (0,9)。断言 p3 `IsBlocked`、伤害 ∈ {27,40.5}；`ProjectileStopped`、停点 ≈ (0,5)；p2 无命中 |
| `SwordQi_Passes_Through_Defender_Facing_Away` | 防守者 yaw0；断言 `IsBlocked=false`、p2 命中、`ProjectileStopped=false` |
| `SwordQi_Passes_Through_NonDefending_Player` | `IsDefending=false`；断言两人都命中、不停 |
| `SwordQi_Closer_Targets_Before_Blocker_Are_Still_Hit` | NPC (0,2) + 防守者 (0,5) + NPC (0,9)；断言 NPC2 命中、防守者格挡、NPC9 不中 |
| `SwordQi_Stops_At_Nearest_Blocker_When_Far_Blocker_Is_Added_First` | 先加 6m 防守者、后加 5m 防守者；断言只有 5m 被结算（距离排序） |
| `SwordQi_Block_Uses_Captured_Origin_After_Caster_Moves` | `DrainResolved(6)` 捕获原点 → `UpdatePosition(10,0)` → `DrainResolved(10)`；断言 5m 防守者仍被格挡（旧实现 63.4° > 60°） |
| `SwordQi_Swing_Block_Outside_Line_Band_Does_Not_Stop_Projectile` | 防守者 (2.05,1.4)、yaw ≈ −124.3°、举盾；NPC (0,9)。距离 2.482<2.5、角度 55.66°<60°、横向 2.05>2；断言挥击格挡但 NPC 命中、`ProjectileStopped=false` |
| `SwordQi_Does_Not_Stop_On_Dead_Defender_Killed_By_Swing` | 防守者 (0,2) HP=10、举盾；NPC (0,9)。挥击 27 杀死；线形对死者不格挡；断言 p3 有“挥击 blocked + 线形 not blocked”两条、NPC 命中、不停 |

**`Server/Server.Tests/Combat/BlockMitigationTests.cs`**

- 用例 ①② 删除耐久断言，保留伤害/余血断言。
- 删除 `Depleted_Shield_Stops_Mitigating_From_The_Fifth_Hit`，替换为
  `Defending_Blocks_Repeatedly_Without_Durability_Limit`：连续 8 次怪物攻击全部 `IsBlocked`、
  每次 3 伤害、HP=76。

**`Server/Server.Tests/SyncCore/CombatRulesTests.cs`**

- `Block_Table_Values_Are_Pinned` 删除两条耐久常量断言；保留 0.3/60。

**协议**

- `Server/Server.Tests/Protocol/CombatMessagesTests.cs`：往返增加
  `ProjectileStopped=true, StopX=1.5, StopZ=6`。
- `Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs`：钉死 tag 5/6/7。

### 7.2 客户端 EditMode

**`CombatNetworkBridgeTests.cs`**

- `OnDamageResult_ProjectileStopped_LocalCaster_Raises_Stop_Event`
- `OnDamageResult_ProjectileStopped_RemoteCaster_Does_Not_Raise`
- `OnDamageResult_ProjectileStopped_NonLineAllSkill_Does_Not_Raise`
- `OnDamageResult_BlockedHit_Without_StopFlag_Does_Not_Raise`（钉死不扫描 `IsBlocked`）
- `OnDamageResult_MultipleBlockedHits_Raises_StopEvent_Once`（挥击+剑气同帧只触发一次）

**`SwordQiProjectileTests.cs`**

- `test_sword_qi_stop_at_point_releases_at_point`
- `test_sword_qi_stop_at_point_behind_releases_immediately`
- `test_sword_qi_stop_distance_resets_on_respawn`（回池后再 `OnSpawn`，旧停点不得残留）

**`SwordQiSpawnerTests.cs`**

- `test_sword_qi_spawner_stop_nearest_picks_projectile_on_its_line`
- `test_sword_qi_spawner_stop_nearest_prefers_projectile_not_passed`
  （qi1 飞到 7m、qi2 到 1m，停点 5m；qi1 越过停点 2m > 1.5m 容差，断言 qi2 `StopDistance≈5`、qi1 保持 Range）
- `test_sword_qi_spawner_stop_without_active_is_noop`

### 7.3 客户端 PlayMode（新增 `BlockStaminaTests.cs`，需 `.meta`）

| 用例 | 断言 |
|---|---|
| `test_block_stamina_online_blocked_hit_consumes_fifteen` | 50 → 35，未力竭 |
| `test_block_stamina_block_cost_exhausts_and_forces_shield_down` | 15 → 0，`IsExhausted`，`_cc.IsDefending=false`，锁存 |
| `test_block_stamina_non_blocked_hit_consumes_nothing` | 50 不变 |
| `test_block_stamina_two_blocked_hits_consume_thirty` | 50 → 20 |
| `test_block_stamina_charges_even_when_local_shield_already_released` | 本地未举盾、服务端 `isBlocked` ⇒ 仍扣 15 |
| `test_block_stamina_charges_while_stunned` | `AddStun(1f)` 后 blocked ⇒ 50 → 35 |
| `test_block_stamina_lethal_block_still_charges_before_death` | Online 模式；精力 15；`OnServerDeath` 回调里读到 0 |
| `test_block_stamina_offline_block_consumes_fifteen` | 先 `IsGrounded=true`、`TryEnterDefend()`；`TakeDamage(50, -forward)` ⇒ 50 → 35 |
| `test_block_stamina_offline_repeated_blocks_never_break_shield` | 连续 4 次格挡共扣 60，精力 40，仍举盾 |
| `test_defend_is_cleared_on_death` | 致死伤害后 `_cc.IsDefending=false` |
| `test_defend_is_cleared_on_respawn` | `ApplyServerRespawn(100)` 后 `_cc.IsDefending=false` |

### 7.4 联网 E2E（扩展 `BlockMitigationE2ETests`）

- `SwordQi_Block_Reports_Stop_To_Caster_And_IsBlocked_To_Target`：
  复用现有 Rig（A (0,0) yaw0；B (0,1.5) yaw180）。A 施放 10002。
  - B 收到 **2 条** `IsBlocked` 命中（挥击 + 剑气）；
  - A 的 `DamageResult.ProjectileStopped=true`、`StopX≈0`、`StopZ≈1.5`；
  - 两条伤害各自 ∈ {27, 40.5}。
- 需要 localhost:8000 的含本改动的服务端（与既有 E2E 同前置）。

### 7.5 验收数字

| 项 | 数值 |
|---|---|
| 剑气 | 12m / 3 段 / 半宽 2m；伤害 90，暴击 40.5 |
| 格挡剑气伤害 | ×0.3 ⇒ 27（暴击 40.5） |
| 每次格挡精力 | 15；挥击+剑气都挡 = 30 |
| 举盾消耗 | 10/s |
| 单命中格挡预算（满精力） | 连续 6 次后剩 10；第 7 次力竭 |
| 持盾 4s + 4 次单命中格挡 | 40 + 60 = 100；第 4 次挡完力竭 |
| 10002 近距离一次施法 | 2 次格挡 = 30 精力 |
| 力竭门槛 | 20% |
| 剑气停点误差 | ≤1m |
| 服务端盾耐久 | 已删除；格挡只看存活 + `IsDefending` + 正面 60° |
| 服务端格挡伤害 | ×0.3 不变 |

### 7.6 运行命令

- 服务端：`dotnet test Server/Server.Tests/Server.Tests.csproj`
- Unity EditMode：`unity-mcp-cli run-tool tests-run --testMode EditMode`
- Unity PlayMode（定向）：`BlockStaminaTests`、`StaminaIntegrationTests`、`BlockMitigationE2ETests`
- 手动冒烟：双端联机，B 举盾挡 A 的剑气 → 剑气在 B 处消失、B 精力 −15、B 身后怪物不掉血；
  单机举盾挡怪 → 精力 −15 且无盾破。

---

## 8. 明确不做

1. 远端举盾状态同步（AOI `Defending` 字段）与远端举盾/格挡动画。
2. 服务端权威精力或服务端盾资源。
3. 火球/陨石/蓄力爆的格挡几何改动。
4. 每次施法最多扣一次精力的上限。
5. 盾破动画/眩晕的保留或重做。
6. 离线 `DefendModifier` 与联机格挡数值的统一。
7. 精力环 UI 改动。
8. 修改 `BlockDamageScale`/`BlockHalfAngleDeg` 的平衡值。
9. 升 `ProtocolVersion`。
10. 其它 LineAll 技能的美术表现（当前只有 10002）。

---

## 9. 风险与限制

| 风险/限制 | 说明 |
|---|---|
| 服务端无盾资源 | 删耐久后，服务端只信客户端 `IsDefending`；改客户端可无限举盾。这是“纯客户端精力权威”的直接后果，与 2026-10-18 精力规格的权威归属一致 |
| 强制收盾同步窗口 | 客户端力竭收盾后，服务端最多还会在一个位置同步间隔内继续判格挡 |
| 视觉停点误差 | 视觉生成点 `(0.8,1.0,0.6)` 与判定原点不同，停点可能差 ≤1m |
| 重叠施法选择 | 两发剑气重叠时按“横向最近、未飞过、优先新发”启发式；不同朝向且横向接近时仍可能选错（已登记，不阻塞） |
| 结果包延迟 | 若结果包晚于剑气飞过停点，选“刚飞过停点最少”的一发立即释放（视觉已在终点附近） |
| 无活跃剑气 | 结果到达时本地没有活跃剑气 ⇒ no-op；不做 pending 队列（避免误停下一次施法） |
| 死亡/复活首帧 | 本次已在死亡与两个复活入口幂等退出防御；服务端 `IsAlive` 同时挡住死者格挡 |
| 离线/联机格挡数值差异 | 离线 80%/160°、联机 70%/120° 为既有差异，本设计不动 |
| 联机 E2E 依赖服务端 | 与既有 `BlockMitigationE2ETests` 相同，需要 localhost:8000 新构建 |

---

## 10. 实现顺序（高层）

1. 协议 + 服务端：`DamageResult` tag 5/6/7、`ApplyHit` 攻击者位置/`IsAlive`、LineAll 排序与吸收、停点字段；服务端测试。
2. 删除耐久：服务端 `PlayerState`/`CombatRules`/`CastHandler`，客户端 `CharacterController`/`Sys3CEntry`/`FSMManager`；回归测试。
3. 客户端格挡扣精：`Sys3CEntry._blockStaminaCost` + 两处挂点 + 死亡/复活退出防御；PlayMode 测试。
4. 客户端视觉：`CombatNetworkBridge` 事件、`ClientNetworkHost` 订阅、`SwordQiSpawner.StopNearest`、`SwordQiProjectile.StopAtPoint`；EditMode 测试。
5. 联机 E2E；全量 EditMode/服务端测试；手动冒烟。
