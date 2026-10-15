# 命中判定时刻对齐与绕背躲怪 设计

> 面向 AI 代理的工作者：本规格续接 `2026-08-27-network-sync-design.md`（§5-P4/P5 服务端权威战斗裁决）与 `2026-10-12-pvp-and-death-presentation-design.md`。读完本文件后用 `superpowers:writing-plans` 生成实现计划。

**目标（一句话）**：把"这一刀挥出去"（决策）与"这一刀打没打中"（判定）拆成两个时刻，让**服务端的判定时刻落在画面上的刀刃命中处**，并因此让怪物攻击第一次**可被走位躲开**（跑出射程，或贴脸冲刺绕背）。

---

## 0. 前置结论（本设计的事实基础）

| # | 事实 | 出处 / 由它推出的结论 |
|---|---|---|
| **F1** | 判定唯一来源是服务端 `CombatResolver.ResolveCast`（纯核心、无 Server3C 依赖、单测直用）。玩家的 `CastRequest` 由 [Server3C.cs:491-493](Server/Server3C.cs) 在**收包路径上立即裁决**，连 tick 都不等 | 本设计改的就是这一处的**时机**，不动它的规则 |
| **F2** | `HitResolver.Resolve` 是**单帧瞬时几何**（XZ 平面），只返回 `Alive` 候选，**无历史位置缓冲、无回滚补偿** | "目标移动了算不算未命中"的答案是"看它有没有逃出**判定那一 tick** 的几何"，不是持续窗口 |
| **F3** | 客户端判定帧的基准是 `hitboxTime = CastTime + hitboxTimings[i]` 对 `_elapsedTime`（技[能开始计时](Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs)），而 `OnSkillActivated` 正打在技能开始那一帧（`SkillCoordinator.TryActivate`） | **表值可直接映射**：`HitDelaySeconds = CastTime + hitboxTimings[0]`，无需换算 |
| **F4** | 资产实测（`Assets/PreRes/SkillsCfg/*.asset`）：BA_Combo1 `[0.3]`、BA_Heavy `[0.35]`、Instant_SkillQ `[0.3]`、**Spin_SkillR `[]`**（旋转不靠判定帧，靠 `UpdateSpinning` 每 `_tickInterval` 出一次） | 延迟**必须逐技能填**，不能取统一常量；spin 是特例 |
| **F5** | 玩家包在**激活瞬间**发出，且 `CastRequest` 不含位置/朝向（`CombatNetworkBridge.NotifySkillCast`）；服务端用的是自己 store 里的值（玩家位置每 66ms 由 `PositionSyncRequest` 上行一次） | 玩家自己的挥砍在服务端是"用陈旧 ≤66ms+延迟 的站位与朝向"判的；且**动画判定帧完全不参与** |
| **F6** | 怪物决策与裁决**同 tick**：`MonsterBtActions.Attack` → `QueueCast` → [Server3C.cs:144-149](Server/Server3C.cs) 同 tick `DrainPendingCasts` + `Resolve`；冷却由 `ResolveCast` 内部的 `RecordCast` 记录 | 怪物攻击的判定窗口 = 0：玩家看到抬手时已经判完了，**躲不掉** |
| **F7** | `CastBroadcast.ServerTimestamp` 在客户端被当作**挥砍剪辑的起始时刻**：`ClientNetworkHost` → `RemoteEntityRegistry.ScheduleAttack` → `NpcAttackSchedule` 到点 → `NpcEntityAnimator.PlayAttack` → `SetTrigger` | 判定帧在剪辑**内部**，所以判定必须落在 `timestamp + 判定帧`。**时间戳语义归位不需要改客户端一行** |
| **F8** | 渲染延迟 = 2 tick（`ClientNetworkManager`，≈132ms@15Hz），位置与攻击排程共用同一根服务器时间轴 | 观察者视角无法再消的残差就是它 |
| **F9** | `HitResolver` 的**显式目标路径只查距离、不查锥角**（`c.Id == targetId && c.Alive && DistXZ <= Range`）；自动目标分支已有锥角 + "零距离视为锥内"的退化护栏 | 怪物的 `Angle=120` 目前是**死参数**；"绕背"之所以不成立就是因为这条 |
| **F10** | 怪物只有**移动时**才转身（`SimpleMoveSystem`，180°/s，朝移动方向）；站定攻击不转向。朝向**每 tick** 同步（`MonsterModule` 的 `entity.UpdatePosition(..., YawOf(m.Rotation), ...)`），且 AOI 的 `moved` 判据**含 `rotY != slot.RotY`**（`AoiBandScheduler`） | **原地转身会以近带 15Hz 广播**——所以"前摇期盯着你转"是看得见的，判定朝向与画面朝向同源 |
| **F11** | 玩家速度：走 5 m/s / 冲刺 8 m/s（`CharacterController`）；怪物转身 180°/s（移动态） | 绕背的可行性可以直接算（见 §3 数值表） |
| **F12** | `Attack` 节点的**距离检查在冷却门之前**；BT 的 Hostile 分支条件是 `AlertLevel == HOSTILE && TargetId.HasValue` | 前摇期间目标跑出射程 → 节点 `return Failure` → 下一 tick 走 Chase（**挥砍表现被截断**）；目标跑出感知半径 → 分支条件不成立 → **走到一半改走回家**。两者都要堵 |
| **F13** | tick 顺序：怪物 tick → drain+resolve → 移除 → `TickRespawns`；注释明写死亡计时"放在怪物 tick 之后——本 tick 由怪物打死的实体即时打上死亡标记" | 判定后移后，`DrainResolved` **必须**排在 `TickRespawns` 之前，否则被延迟判定的死亡要多等一个 tick |
| **F14** | `ResolveCast` 首行即 `caster == null` / `!caster.IsAlive` 校验；`HitResolver` 只返回 `Alive` 候选 | 死亡取消前摇（打断抬手）**不需要新语义**，只需在判定时**复查一次** |

---

## 1. 目标与成功判据

### 目标

1. **判定时刻与画面上的刀刃命中对齐**——玩家自己的挥砍、观察者看到的怪物挥砍，三端同一套语义：`广播时刻 = 挥砍开始`、`伤害时刻 = 广播 + 判定帧`
2. **怪物攻击第一次可被躲开**：跑出射程（原有），或**贴近冲刺绕出判定锥**（新增）
3. **决策与判定分离**被显式建模：冷却、表现、伤害三者的时刻关系写死在一条链上

### 成功判据（这就是"完成"的定义）

- 怪物挥砍动画的命中帧与玩家受击飘字落在**同一时刻**（±1 渲染延迟 ≈132ms）
- 玩家挥砍：伤害出现的时刻落在**本地动画判定帧**上（残差 = 一次上行延迟）
- 贴脸（≤1.75m）冲刺绕圈 → 服务端判 **Miss**；正面站定 → **必中**
- **前摇期打死怪 → 无伤害**（打断抬手成立）
- 回归：`Server.Tests` / EditMode / PlayMode 三套件全绿

---

## 2. 范围边界

### 做

| 层 | 内容 |
|---|---|
| **纯核心** | `CombatRules` 表加 `HitDelaySeconds`；`HitResolver` 显式目标路径加锥角；`CombatResolver` 拆成 `Decide` / `DrainResolved` + 内部 pending 队列 |
| **服务端接线** | `CastHandler` 拆成"决策广播挥砍"与"判定广播伤害"；`Server3C` 的 tick 顺序（`DrainResolved` 在 `TickRespawns` 之前）+ `NotifyCastResolved` 回调 |
| **怪物** | `MonsterState.AttackPending`（前摇 = 承诺）；前摇期以 90°/s 朝目标转向；BT 的 Hostile 条件放宽 |
| **客户端** | **零改动**（仅注释/断言口径跟随） |

### 明确不做

- **多段命中的服务端建模**（spin 每 0.2s 一段）——现状是"单次瞬时 AOE"，本次保持，进 backlog
- **"被打断"取消前摇**（受击硬直 / 眩晕）：`HitDisplayTicks` 窗口只跳过 BT，**不取消**已承诺的刀。只有**死亡**取消（人类裁定）
- **玩家侧的滞后补偿 / 位置回滚**：位置与朝向陈旧不消（低延迟 demo 下收益不可见，风险最高）
- **怪物之间的互殴 / 怪物 AOE / 仇恨表**
- **锥边缘 ±36° 模糊带的消除**（见 R2，它是渲染延迟的固有产物）

---

## 3. 架构

```
[纯核心] CombatResolver
   Decide(casterId, req, tick) → {Accepted, RejectReason, Broadcast}
        · 保留全部既有校验（存在/存活/表内/形状防篡改/冷却）
        · caster.RecordCast(skillId, tick)          ← 冷却起点 = 决策（D-3）
        · Broadcast.ServerTimestamp = 决策时刻       ← "挥砍开始"（表现）
        · 入队 {casterId, req, resolveTick = tick + HitDelayTicks(entry)}
   DrainResolved(tick) → [{casterId, req, Hits}]      ← 只有 resolveTick <= tick 的出队
        · 复查施法者存活（F14：死了就丢这一刀）
        · HitResolver 几何（距离 + 锥角）+ 服务端掷骰 + 扣血
            ↓
[服务端接线] Server3C.tick:
   monsterModule.Tick(tick)                            → 决策入队（QueueCast）
   foreach pending cast: castHandler.Decide(...)       → 校验 + 冷却 + 广播挥砍
   foreach resolved:     castHandler.BroadcastDamage()  → DamageResult
   monsterModule.NotifyCastResolved(npcId)             → 清 AttackPending
   TickRespawns(tick)                                  ← **必须在 DrainResolved 之后**（F13）
            ↓
[客户端] 零改动：
   CastBroadcast.ServerTimestamp = 挥砍剪辑起始时刻（F7 本来就是这么用的）
   DamageResult 到达即表现（飘字 / 受击 / HUD）——现在它落在刀刃命中那一刻
```

### 为什么延迟由服务端表自持、不随包上线

`CastRequest` 已经带着 `Shape` 上线并由服务端按表卡上限（防篡改）。延迟**不**跟着上线的理由：它是**服务器时刻**的口径（决定判定发生在哪个 tick），不是客户端的表现参数；让它上线只会多一个防篡改面与一个两端漂移源。客户端动画资产改了，改的是**表的注释来源**，不是协议。

### 为什么广播必须提前到"决策"时刻

因为 F7：客户端拿 `ServerTimestamp` 当**剪辑起始**用。判定后移而广播不动，等于"挥砍从判定那一刻才开始播"——错位只会从"伤害早 0.3s"翻成"伤害晚 0.3s"。`广播 = 挥砍开始` 是唯一自洽的语义。

### 为什么前摇期要锁住行为树

F12：前摇期内目标一旦跑出射程，`Attack` 会返回 `Failure` → 序列失败 → 下一 tick Chase 让怪物**走起来**、`ActionState` 被写成 `Run`（挥砍表现被截断），可那笔 pending 的刀还在队列里——目标若又跑回射程内，就会出现"**怪已经收刀了你还挨了一下**"的假命中。所以：**前摇 = 承诺**，决策后怪物站定、转身朝向目标，不受距离变化影响。

### 绕背躲怪的可行性（F11 代入的数值）

判定条件：`距离 ≤ Range` **且** `夹角 ≤ Angle/2 = 60°`。前摇 0.35s，前摇期转身 **90°/s**（人类裁定，D-7）：

| 绕圈半径 | 冲刺角速度 8/r | 前摇内相对转角 (ω−90)×0.35 | 结果 |
|---|---|---|---|
| 1.0 m | 458°/s | 129° | **绕出锥外 → 空挥** |
| 1.5 m | 305°/s | **75°** | **绕出锥外 → 空挥** |
| 1.75 m | 262°/s | 60° | 临界 |
| 2.0 m | 229°/s | 49° | 躲不掉 |

- **冲刺绕圈的半径门槛 ≈ 1.75m**；走（5 m/s）在 1.09m 内才勉强够——**绕背实质上是冲刺专属动作**
- "单刀内跑到正后方"需要 180° 弧长：1m 半径需 0.39s > 0.35s，**差一点**；更远做不到。所以绕背的真实形态是**连续博弈**（一直贴脸绕 → 每刀都空），不是一刀绕到背后
- 转身速度从 180°/s 降到 90°/s 是**唯一**把门槛从 1.2m 放宽到 1.75m 的改动；另一顺位旋钮是收窄锥角（R2）

---

## 4. 组件分解

| # | 组件 | 位置 | 职责 | 可独立测试 |
|---|---|---|---|---|
| **C1** | `SkillEntry.HitDelaySeconds` + `CombatRules.HitDelayTicks()` | `Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs` | 逐技能的判定延迟（表驱动）；**换算沿用 `CooldownTicks` 的 `ceil(s × 15)`** | EditMode / Server.Tests 表用例 |
| **C2** | `HitResolver` 显式目标加锥角 | `Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs` | 距离 + 锥角；沿用零距离退化护栏 | 双方 `HitResolverTests` |
| **C3** | `CombatResolver` 两段化 + pending 队列 | `Server/Handler/CastHandler.cs` | `Decide` / `DrainResolved`；队列是纯核心内部状态 | Server.Tests 直调 |
| **C4** | `CastHandler` 广播拆分 | 同上 | 决策广播挥砍（caster + 观察者）/ 判定广播伤害（caster + 受击者） | Server.Tests |
| **C5** | `Server3C` tick 顺序 + 回调 | `Server/Server3C.cs` | `DrainResolved` 在 `TickRespawns` 前；清 `AttackPending` | Server.Tests 集成 |
| **C6** | `MonsterState.AttackPending` | `Server/Monster/MonsterState.cs` | 前摇 = 承诺的载体 | Server.Tests |
| **C7** | `MonsterBtActions.Attack` 前摇锁 + 转身 | `Server/Monster/MonsterBtActions.cs` | 首行查在飞攻击 → `Running` 站定；以 90°/s 朝目标转向 | Server.Tests |
| **C8** | BT Hostile 条件放宽 | `Server/Monster/MonsterModule.cs` | `TargetId.HasValue \|\| m.AttackPending`——前摇不中途改走 | Server.Tests |
| **C9** | 客户端 | —— | **零改动**；只更新"时间戳语义"的注释口径 | —— |

---

## 5. 错误处理

| 场景 | 处理 |
|---|---|
| 判定 tick 时施法者已死 / 已被移除（`NpcDespawn`） | 丢弃该刀，**不下发** `DamageResult`（F14 的复查点） |
| 判定 tick 时目标已死 | `HitResolver` 只返回 `Alive` 候选 → 自然空挥，无需特判 |
| 目标在前摇期跑出感知半径 | `AttackPending` 让 Hostile 分支条件仍成立 → **不中途改走**；这把刀仍按承诺裁决（多半空挥） |
| 目标在前摇期跑出射程 | 前摇锁生效（不 Chase、不截断），裁决时按几何判空 |
| 施法者在判定前被击晕 / 受击硬直 | **不取消**（明确不做，D-4）；受击窗口只跳过 BT |
| 零延迟技能（spin，表值 0） | `DrainDue` 用 `resolveTick <= tick` 出队 → **同 tick 内即出队**，今天的"决策即裁决"语义原样保留 |
| 玩家在 tick 之间发包 | `decideTick = _server.CurrentTick`，`resolveTick = decideTick + HitDelayTicks` |
| 队列中累积（客户端狂发 + 冷却 0 的技能） | 与今天同量级（今天也是逐包裁决）；不新增背压机制，但队列需在实体移除时丢弃（上表第 1 行） |

---

## 6. 依赖序任务清单（不排日历）

| # | 任务（**编号从 N 起，避免与项目阶段 P0–P8 冲突**） | 规模 | 验收 |
|---|---|---|---|
| **N1** | 纯核心：`CombatRules.HitDelaySeconds`（逐技能表值）+ `HitResolver` 显式目标锥角（C1/C2） | 小 | 双方单测：锥内命中 / 锥外空 / 零距离退化仍在；表约束用例（`HitDelay ≤ Cooldown`、`MaxRange ≥ attackRange`） |
| **N2** | 纯核心：`CombatResolver` 拆 `Decide` / `DrainResolved` + pending 队列（C3） | 中 | Server.Tests：前摇内目标移出射程 → 空；施法者死亡 → 丢弃；决策即冷却；**零延迟同 tick 出队** |
| **N3** | 服务端接线：`CastHandler` 广播拆分 + `Server3C` tick 顺序 + `NotifyCastResolved`（C4/C5） | 中 | Server.Tests 集成：挥砍广播在决策 tick、伤害广播在判定 tick；死亡标记不多等 tick |
| **N4** | 怪物前摇：`AttackPending` + 90°/s 转身 + BT 条件放宽（C6/C7/C8） | 中 | Server.Tests：前摇期不重复入队、不 Chase、不中途改走；转身角速度可用 |
| **N5** | 既有测试迁移 + 新增用例（§7） | 中 | 四套件全绿；组合用例覆盖"绕背 → Miss" |
| **N6** | 跑起来看（WebGL + PC 双端） | 小 | §1 的四条成功判据**逐条肉眼确认**——这是唯一能证明"判定帧对齐"成立的验收 |

---

## 7. 测试策略

| 层 | 方式 |
|---|---|
| 纯核心（表 / 几何） | 双端 `HitResolverTests` + `CombatRulesTests`：锥角新规则、零距离退化护栏、表约束遍历 |
| 纯核心（时机） | Server.Tests 直调 `Decide` / `DrainResolved`：前摇窗口内的四种结局（空 / 中 / 施法者死 / 目标死） |
| 服务端接线 | `CastHandlerTests` + `MonsterCombatIntegrationTests`：广播的两个时刻、tick 顺序、`NotifyCastResolved` |
| 怪物行为树 | `MonsterModuleTests`：前摇锁（不 Chase / 不重复入队 / 不中途改走）；**既有 `Hit_Window_Expiry…` 那条靠真实裁决链的用例必须继续用它** |
| 回归 | 三配置并集（EditMode / Server.Tests / PlayMode）全绿 |
| 端到端 | N6 手动：怪挥砍与飘字同帧；贴脸冲刺绕圈 → 空挥；正面站定 → 必中；前摇期打死 → 无伤害 |

**既有测试的迁移面（已定位，机械但面广）**：`CombatResolverNpcTests`、`CastHandlerTests`、`MonsterCombatIntegrationTests`、`MonsterModuleTests` —— 全部从"`ResolveCast` 一步到位"改成"`Decide` → 推进到判定 tick → 取结果"。**先迁移到全绿，再改行为**（R1）。

---

## 8. 风险清单

| # | 风险 | 影响 | 应对 |
|---|---|---|---|
| **R1** | **既有测试面广**：4 个服务端测试文件都按"决策即裁决"写 | 迁移与行为改动混在一起，红绿分不清 | N5 之前先做**纯迁移**（同样的断言、两步化调用），跑绿后再叠行为 |
| **R2** | **锥边缘的 ±36° 模糊带**：判定朝向与玩家看到的朝向差 ≈ 1 tick 量化 + 132ms 渲染延迟（180°/s 下 36°，90°/s 下 18°） | 玩家"看着躲开了还挨打" | 已知且不可消（渲染延迟的固有产物）。手感不对时旋钮顺序：**先降前摇转身速度 → 再收窄锥角**；两条都是单行改动 |
| **R3** | **怪物 `HitDelay = 0.35s` 是占位值**（人类裁定，D-8）：NPC 攻击剪辑的判定帧**未实测** | 与画面上的刀刃命中仍错位 | 表值集中在 `CombatRules` 一张表里，实测后逐条替换；注释标注"来源"。**不要**把它当成已知量写进别的文档 |
| **R4** | **前摇锁的副作用**：目标彻底消失（离线 / 死亡 / 走出感知）后，怪物仍会把这刀"挥完" | 观感上"对着空气挥一刀" | 这正是"前摇 = 承诺"的代价，且比"挥到一半改走回家"更合理（F12）。若观感不可接受，改动点是 Hostile 条件，不是判定 |
| **R5** | **pending 队列成为新的服务端状态**：实体移除 / 断开后残留 | 内存泄漏、对已不存在的实体裁决 | 出队时复查施法者（F14 那条），丢弃即可；N2 的单测显式覆盖"施法者已移除" |
| **R6** | **判定后移 0.35s**：击杀时刻整体推迟 | 既有 e2e / 压测的时间窗断言可能偏紧 | N5 里检查所有"多少 tick 后应发生X"的断言，必要时放宽一个 `HitDelayTicks` |
| **R7** | **怪物攻击的观感变化**：目标留在射程内时，冷却 1~2s 意味着挥空后仍要站定到冷却结束 | 玩家可能觉得怪"变迟钝" | **今天就是这个行为**（冷却门未过时 `return Running` 站定），本次不改；前摇锁只在 0.35s 窗口内生效力，窗口一过目标不在射程内就恢复追击 |

---

## 9. 决策记录

| # | 决策 | 理由 |
|---|---|---|
| **D-1** | **判定时刻对齐到动画判定帧**（服务端延迟裁决），而不是客户端预测 | F5/F7：客户端预测会与"服务端唯一裁决"（P4 既定）冲突；延迟裁决只是把既有裁决**挪到正确的时刻** |
| **D-2** | 延迟**由服务端表自持**，不随包上线 | 它是服务器时刻的口径，不是表现参数；上线只多一个防篡改面与漂移源 |
| **D-3** | **冷却起点 = 决策时刻**（不是判定时刻） | 冷却门是"防连发"的闸，决策就产生了这一刀；且与既有"空挥也冷却"（D-P4-5）语义一致。副作用：`Decide` 后同 tick 重施仍被拒——既有测试语义不变 |
| **D-4** | 判定前**施法者死亡 → 丢弃**；**受击硬直 / 眩晕 → 不取消**（人类裁定） | 死亡取消让"打断怪物抬手 = 免伤"成立（廉价、手感好）；"被打断"要引入技能打断矩阵的服务端语义，属 backlog |
| **D-5** | `CastBroadcast` 提前到**决策时刻**，其 `ServerTimestamp` 语义 = **挥砍剪辑起始** | F7：客户端本来就是这么用它的。判定后移而广播不动只会把错位翻个方向 |
| **D-6** | **绕背用锥角**：给 `HitResolver` 的显式目标路径补上锥角检查（与自动目标分支对齐） | F9：锥角是死参数等于"站着不动也必中"。补上后"绕背"才成为一条真实的躲避路径，且复用既有的零距离退化护栏 |
| **D-7** | 前摇期转身速度 **90°/s**（人类裁定；移动态仍 180°/s） | 把冲刺绕圈的半径门槛从 1.2m 放宽到 1.75m，让"绕背"成为可执行的操作而不是理论值；代价是怪物看起来"盯着你但不跟得上" |
| **D-8** | 怪物技能 `HitDelay = 0.35s` **占位**（人类裁定） | NPC 攻击剪辑判定帧未实测；先让链路跑通，实测后逐条替换（R3） |
| **D-9** | **零延迟项保持同 tick 出队**（spin 表值填 0） | 让改动可以逐技能灰度，且 spin 的多段命中本就是未建模的现状——**不借本次机会改它** |
| **D-10** | 前摇 = **承诺**：`AttackPending` 锁住行为树，只有死亡能取消 | F12：不锁就会"收刀了还挨打"（假命中）与"挥到一半改走回家"（表现断裂）两种缺陷 |
| **D-11** | 客户端**零改动** | F7：时间戳语义归位后，`ScheduleAttack` 的行为本来就是期望行为。改动面留在服务端，是为了让"判定在哪一侧"这件事对读者一眼可辨 |
