# 属性剑气 Plan 06：验收缺口与稳定性实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 补齐 Plan 05 登记但未执行的设计验收场景，并补 ORCA 邻居用例；不新增战斗机制。

**架构：** 以现有测试基建为主：三客户端 PlayMode 复用 `ThreeClientElementalAcceptanceTests` 的真实 Server3C 场景；收包层注入复用 `NetworkConsumptionInjectionTests`；AOI/版本连续与服务端动作链用 xUnit；移动 `NpcBody` 纠偏在 `PlayerControlInertiaTests` 的夹具上扩展。只有验收暴露真实缺陷时才改生产代码。

**技术栈：** Unity 2022.3.25f1、NUnit PlayMode/EditMode、.NET 8 xUnit、真实 `Server3C:8000`、KCP/WebSocket 收包注入。

---

## 范围与边界

本计划只覆盖**无需美术资源或产品决策**的验收缺口：

- 三端 DoT 每跳一致与死亡投影一致（端到端末跳致死时刻不在本计划，见下）。
- 麻痹取消前摇 `Cancelled` 链。
- 桥层延迟/乱序注入、换命旧包、模型未加载。
- AOI 快速出入/复活版本连续。
- 移动 `NpcBody` + 位置上报/纠偏**机制**联测（使用测试本地 `IDamageable` stub；生产 NpcBody 推挤不在本计划）。
- ORCA 减速 `maxStep` 邻居用例。

以下**不在本计划**，保留到 Plan 07/backlog：
- 状态结束时钟偏移注入（给两台客户端注入不同时钟/渲染延迟）。
- KCP/WebSocket **传输层**延迟注入；本计划只覆盖桥消费层的有序/延迟重投。
- 三端同一次末跳致死的死亡时刻（需要服务器 HP 注入/测试钩子）。
- 真实连接代次与旧位移预测的宿主级隔离；桥层只覆盖换命 `LifeId` 与新旧桥状态。
- 离线四元素共享运行时、Addressables 元素资源/生产 loader/美术签收、远端武器模型、
  `_pushVector` 对 `NpcBody` 的产品行为、`NpcAction` 驱动攻击变体、Plan 04 残余 M 项。

## 全局约束

- 只在 worktree `E:/CodeForJob/Cool/.claude/worktrees/elemental-sword-qi-foundation` 工作；不 `cd` 主仓库、不 push。
- 服务端测试：`bash tools/test-server-inprocess.sh`（testhost `Win32Exception(5)` 时）。
- Unity 测试优先 `bash tools/unity-tests.sh <Mode> <Class>`；MCP 不可用时用 batchmode，**不加 `-quit`**，XML 落盘后结束进程；跑完恢复 `Assets/Packages/**`、`ProjectSettings/**`、`Assets/PreRes/Font/ZhunYuan SDF.asset` 等 batchmode 副作用，绝不提交。
- 已知环境失败：`RemoteEntityRegistryTests.Bot_Radius`、`PlayerBodyTintTargetTests` 的 batchmode PPtr、`NpcCatalogAssetTests` 缺发布资产、`ResourceAsyncLifecycleTests` 异步时序、`MonsterWorldTests` 导航巡逻债、`PlayerJumpBodyCollisionTests` batchmode 0/3。不得把它们写成回归或通过。
- 所有新测试必须有可证伪断言：精确数值、`EventId`/`CastId`/`CombatVersion`/`LifeId`、事件顺序；禁止"最终一致"这类模糊断言。
- 生产代码改动只允许由失败的验收测试驱动，并在报告中写明触发断言。

---

### 任务 1：三端 DoT 每跳一致与死亡投影一致

**范围裁定（2026-11-20）：** 端到端“末跳致死”无法在不引入服务器 HP 注入/测试钩子的前提下稳定构造：
B 为 720 HP 的联机玩家，10002 每次直伤 90×2（暴击 15%），灼烧仅 8/秒；把 B 打到最后一跳必死
需要精确控制直伤/跳伤顺序，生产数值下不可确定复现。因此本任务拆成两层：

1. **服务端末跳致死**由既有 `DotDamageTests.Burn_Tick_That_Kills_Player_Clears_Statuses_And_Stops`
   证明（`MaxHp=8`，一跳致死，断言 HP=0、只有一条 DotHit、状态清空、死后不再跳）。
2. **三端**只证明每跳数值一致与死亡投影一致（致死来源不限）；不得用方法名或文档宣称
   “三端末跳致死时刻”已验收。该端到端场景登记 Plan 07，需要服务器测试钩子或可注入低血量。

**文件：**
- 修改：`Assets/Scripts/Tests/PlayMode/ThreeClientElementalAcceptanceTests.cs`
- 可能修改：`Server/Status/StatusTickService.cs`、`Server/Handler/CastHandler.cs`（仅当测试暴露缺陷）

- [ ] **步骤 1：写失败的三端 DoT 场景**

在 `ThreeClientElementalAcceptanceTests` 增加第二个 `[UnityTest]`，方法名：
`FireDot_Ticks_Are_Consistent_And_Death_Projection_Is_Consistent`。

复用 `NewClient` 与 `RunThunderAsync`/`RunFireAsync` 的三客户端登录/装备模式，但 A 装备火剑（`ElementId=2`），施放 `10002` 命中 B。断言：

1. A/B/C 收到的 `DamageResult` 中，`DamageKind=Dot` 的命中按 `EventId` 一一对应，逐条严格相等：
   `Damage`、`TargetRemainingHp`、`TargetLifeId`、`CombatVersion`、`EventSeq`、`CastId`、`SourcePlayerId`。
2. DoT 跳伤不产生 `IsBlocked=true`，且不推进 B 的 `ControlVersion`/`ActionLock`（跳伤不触发受击硬直）。
3. 跳数 = `3`（火 3 秒 / 1 秒间隔，末跳在 `EndTick`），相邻跳 `ServerTimestamp` 间隔在
   `0.85–1.15s`；末跳后 B/C 的 311 投影中 `EffectId=201` 恰好移除一次。
4. 死亡投影一致：让 A 连续火剑命中直到 B 死亡（每轮等 `CastConfirm` + DoT 跳），断言 A/B/C
   观察到 B 的 `CombatStateUpdate.Dead=true` 的 `CombatVersion`/`LifeId` 严格相等；
   记录致死 `HitEntry`（可能是直伤或 DoT），三端逐字段一致；死亡后不再收到 B 的 DoT 命中。
   不要求致死来源为 DoT——末跳致死由服务端用例单独证明（见范围裁定）。

失败判据：当前没有三端 DoT 场景，测试不存在。

- [ ] **步骤 2：运行并确认失败**

```bash
bash tools/unity-tests.sh PlayMode ThreeClientElementalAcceptanceTests
```

预期：新方法不存在或断言失败。

- [ ] **步骤 3：实现测试，按需修生产缺陷**

只允许改被断言打红的最小路径。若某端缺 DoT 命中，先补该端消费；若末跳多一跳/少一跳，
修 `StatusTickService` 的到期顺序（先排定跳、再移除），不得放宽断言。

- [ ] **步骤 4：运行通过**

```bash
bash tools/unity-tests.sh PlayMode ThreeClientElementalAcceptanceTests
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~DotDamageTests"
```

预期：三客户端 2/2（原雷场景 + 新火场景）；`DotDamageTests` 全绿。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Tests/PlayMode/ThreeClientElementalAcceptanceTests.cs
git commit -m "test(playmode): accept three-client dot ticks and death projection"
```

---

### 任务 2：麻痹取消前摇三端 `Cancelled` 链

**文件：**
- 修改：`Server/Server.Tests/Handler/CastCancelBroadcastTests.cs` 或新增 `Server/Server.Tests/Handler/CastCancelDuringWindupTests.cs`
- 修改：`Assets/Scripts/Tests/PlayMode/ThreeClientElementalAcceptanceTests.cs`
- 可能修改：`Server/Handler/CastHandler.cs`、`Server/Server3C.cs`、`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs`

- [ ] **步骤 1：先写服务端确定性用例**

新增 xUnit：B 在 tick `T` 接受 `10002`（`Launched=false`，前摇未到），A 的雷 10002 在
`T+1` 命中 B，触发 `ApplyPlayerControl(ActionLock=true)` → `CancelCaster(B)`。断言：

1. 返回的 `CastCancelBroadcast` 的 `CastId == B.CastId`、`CasterLifeId == B.LifeId`、
   `EffectiveTick == 命中 tick`、`Reason=Hit`。
2. B 的 `PendingCast.Spent=true`，`ActiveCast=null`，后续 `DrainResolved` 不再产生 B 的
   `DamageResult`，也不生成 `ProjectileLaunchBroadcast`。
3. 若 B 已经发射（`Launched=true`），同一命中只发取消广播，不终止已发射投射物；
   后续段仍按既有规则结算（与 Plan 02 语义一致）。
4. 连续两次 B 施法，只取消指定 `CastId`，另一个继续。

- [ ] **步骤 2：运行确认失败/通过**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~CastCancel"
```

预期：若语义已实现则通过；若缺少 `EffectiveTick`/按 `CastId` 精确取消则失败。

- [ ] **步骤 3：写三端 PlayMode 场景**

新增 `Thunder_Cancels_Windup_Cast_On_All_Clients`：B 先开始 `10002`，A 的雷命中后：

1. A/B/C 收到同一 `CastId` 的 `CastCancelBroadcast`，`EffectiveTick` 相等。
2. B/C 的 `RemoteCastPresenter` 按 `CastId` 停止；B 的 `DamageResult` 不含 B 取消后的命中，
   也不含未发射剑气。
3. A 的雷剑挥击+剑气仍按自己的 `CastId` 产生 2 条命中（与现有雷场景一致）。
4. C 的 `RemoteEntityAnimator` 不再播 B 的攻击后续段。

- [ ] **步骤 4：运行三端测试**

```bash
bash tools/unity-tests.sh PlayMode ThreeClientElementalAcceptanceTests
```

预期：3/3（雷状态、火 DoT、麻痹取消）。

- [ ] **步骤 5：Commit**

```bash
git add Server/Server.Tests/Handler Assets/Scripts/Tests/PlayMode/ThreeClientElementalAcceptanceTests.cs
git commit -m "test(combat): accept paralysis cancel during windup across three clients"
```

---

### 任务 3：延迟注入、断线重进旧包与模型未加载

**文件：**
- 修改：`Assets/Scripts/Tests/Editor/SyncLayer/NetworkConsumptionInjectionTests.cs`
- 可能修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`、
  `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`、`ClientNetworkHost.cs`

- [ ] **步骤 1：补延迟/交错用例**

只通过桥的 `OnMessage`/`Update` 入队，不新造网络层。新增用例：

1. **换装确认/施法交错**：`EquipWeaponConfirm` 延迟于 `CastConfirm` 到达；旧装备版本不得覆盖
   新装备，施法确认仍带原 `EquipmentRevision`。
2. **发射晚于格挡结果**：`ProjectileStopBroadcast` 先于 `ProjectileLaunchBroadcast` 到达；
   晚到 launch 不得重建已停止视觉。
3. **旧伤害晚于复活**：`RespawnNotice` 后到达旧 `LifeId` 的 `DamageResult`，不得扣血/飘字。
4. **较新血量早于状态消息**：较早 `CombatVersion` 的 `CombatStateUpdate` 后到，不得回退
   已应用的血量/状态。
5. **延迟但同版本有效**：同一 `EventId` 延迟到达仍只消费一次。

- [ ] **步骤 2：断线重进旧包**

模拟 `ClearDedupe` + 新连接代次，再投递旧会话的 `DamageResult`/`CastBroadcast`/`CombatStateUpdate`；
断言全部丢弃，且旧模型回调/旧位移预测不生效（若桥不负责模型回调，断言桥层事件不触发）。

- [ ] **步骤 3：模型未加载**

在 `RemoteEntityRegistry` 尚无视图时投递状态 `CombatStateUpdate` 与 DoT `DamageResult`；
随后创建视图并投递同一版本基线，断言状态最终正确挂载、DoT 数值不重复扣减/不丢失
（丢失需由后续 311 基线补齐）。若当前实现无法恢复，修基线重放，不得只改测试。

- [ ] **步骤 4：运行**

```bash
bash tools/unity-tests.sh EditMode NetworkConsumptionInjectionTests
```

预期：原 14 条 + 新增用例全绿（目标 ≥ 22）。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Tests/Editor/SyncLayer/NetworkConsumptionInjectionTests.cs
git commit -m "test(sync): inject delayed, stale-session and missing-view messages"
```

---

### 任务 4：AOI 快速出入/复活版本连续

**文件：**
- 修改：`Server/Server.Tests/Aoi/AoiBroadcasterTests.cs` 或新增 `Server/Server.Tests/Aoi/AoiVersionContinuityTests.cs`
- 可能修改：`Server/Aoi/AoiBroadcaster.cs`、`Server/Aoi/CombatProjectionBuilder.cs`

- [ ] **步骤 1：写失败用例**

构造观察者 O 与实体 E：

1. O 在 80/90/91m 边界快速进出 3 次；每次进入基线携带当前 `CombatVersion/LifeId`，
   退出后旧增量不再投递给 O。
2. E 死亡→复活（`LifeId++`）→再次进入 O 的视野；新基线必须是新 `LifeId`，
   旧 `LifeId` 的 `CombatStateUpdate` 不得复活旧状态/旧装备。
3. E Evade 到家：`CombatVersion` 严格递增，基线状态列表与服务器最终状态一致。
   回池重生按生产语义是**新实体 id**（`MonsterModule` 每次重生取新 id，新实体 `LifeId=1`、
   `CombatVersion=0`）：断言旧 id 立即停止投递、新 id 首帧是完整基线；**不承诺跨实体 id
   的 `CombatVersion` 单调**（客户端按实体 id 重建视图，旧 id 不得复活）。
4. O 断线重进（`RemoveObserver` → `AddObserver`）；新会话首条基线是完整投影，
   不残留旧状态。

断言直接比较 `CombatProjection` 的 `LifeId/CombatVersion/Hp/Statuses/EquipmentSummary`，
以及 `AoiBroadcaster` 实际发送的消息计数（不得多发旧增量）。

- [ ] **步骤 2：运行**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Aoi"
```

预期：新增用例全绿；若失败，修基线/版本合并，不放宽断言。

- [ ] **步骤 3：Commit**

```bash
git add Server/Server.Tests/Aoi
git commit -m "test(aoi): assert version continuity across re-entry and respawn"
```

---

### 任务 5：移动 `NpcBody` + 位置上报/纠偏联测

**文件：**
- 修改：`Assets/Scripts/Tests/PlayMode/PlayerControlInertiaTests.cs`
- 可能修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/*`、`Assets/Scripts/Hotfix/GameSystems/Sys3C/*`

- [ ] **步骤 1：构造移动 NpcBody**

在现有静态盒夹具基础上，让 `NpcBody` 以 1.5 m/s 沿 X 轴往返或单向移动，持续 2 秒，
本地玩家保持贴附输入；启用 `Sys3CEntry` 的推挤与 `MovementReporter`（不复用静态盒
`Sys3CEntry` 关闭的旧夹具）。断言：

1. 本地 `CharacterController` 无持续往返：2 秒内玩家相对怪物的 XZ 偏移净变化
   ≤ 0.1m，且方向翻转次数 ≤ 1。
2. 上报位置与服务端纠偏目标差 ≤ 0.5m（纠正阈值）；纠偏次数不超过超预算包数。
3. 怪物停止后玩家稳定在最后位置，无残余推挤速度。

- [ ] **步骤 2：运行**

```bash
bash tools/unity-tests.sh PlayMode PlayerControlInertiaTests
```

预期：原 3 条 + 新移动用例全绿（目标 4/4）。

- [ ] **步骤 3：若失败**

只允许修推挤/纠偏交互的最小路径；不得把移动 NpcBody 改成静态盒，也不得放宽净变化阈值。

- [ ] **步骤 4：Commit**

```bash
git add Assets/Scripts/Tests/PlayMode/PlayerControlInertiaTests.cs
git commit -m "test(playmode): couple moving npc body push with server correction"
```

---

### 任务 6：ORCA `maxStep` 邻居用例与文档收口

**文件：**
- 修改：`Server/Server.Tests/Monster/MonsterStatusControlTests.cs`
- 修改：`docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md`
- 修改：`Server/README.md`（若行为证据变化）

- [ ] **步骤 1：写邻居用例**

在 `MonsterStatusControlTests` 增加：怪物 A 被冰减速（`SpeedScale=0.7`），邻近怪物 B 施加
ORCA 避让；断言 A 的实际 `maxStep` = 基础 `maxStep × 0.7`，且避让方向不因缩放反转。
无邻居对照组仍按原期望位移；两组差值随 `SpeedScale` 线性变化。

- [ ] **步骤 2：运行**

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterStatusControlTests"
```

预期：新增用例全绿。

- [ ] **步骤 3：更新文档**

路线图把 Plan 06 已完成的项勾掉，写出证据（测试类/断言/命令）；`Server/README.md`
把“ORCA maxStep 同缩由 Plan 05 压测验收覆盖”改为本用例的具体证据。

- [ ] **步骤 4：全量验证**

```bash
bash tools/test-server-inprocess.sh
bash tools/unity-tests.sh EditMode NetworkConsumptionInjectionTests
bash tools/unity-tests.sh PlayMode ThreeClientElementalAcceptanceTests
bash tools/unity-tests.sh PlayMode PlayerControlInertiaTests
```

预期：服务端全绿；EditMode 注入类全绿；PlayMode 三类全绿（含既有环境失败说明）。

- [ ] **步骤 5：Commit**

```bash
git add Server/Server.Tests/Monster/MonsterStatusControlTests.cs docs/superpowers/plans/2026-11-14-elemental-sword-qi-roadmap.md Server/README.md
git commit -m "test(monster): assert orca max step scales with status slow"
```

---

## 完成定义

- 六个任务的验收测试全部存在并通过；失败必须由生产缺陷修复，不得放宽断言。
- 三端 DoT（每跳/死亡投影）、麻痹取消、延迟/断线/模型未加载、AOI 版本连续、移动 NpcBody、
  ORCA 邻居用例均在路线图标记完成并附证据。
- **明确未验收**：三端同一次末跳致死的死亡时刻（需要服务器 HP 注入/测试钩子），登记 Plan 07。
- 未把离线共享运行时、Addressables 美术、远端武器模型、`NpcBody` 推离写成已完成。
- 服务端全量、Unity 目标类与 PlayMode 目标类通过（环境失败单独列出）。
- Unity batchmode 副作用未提交。
