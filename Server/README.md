# Server —— 服务端

## 运行

```bash
dotnet build Server/Server3C.csproj
dotnet Server/bin/Debug/net8.0/Server3C.dll 8000
bash tools/test-server.sh
```

> 用 `dotnet <dll>` 而不是 `dotnet run`：本机 `dotnet run` 的 apphost 会让
> `HttpListener.Start` 抛「句柄无效」；普通环境两者等价。

`Program.cs` 解析端口与压测参数：`--bots N`、`--no-avoid`、`--probe`、
`--monsters N`、`--map <id>`、`--no-nav`。

## 架构

- `GameLoop.cs`：单线程主循环（约 15Hz / 66ms）。IO 线程只 Enqueue，唯一消费者独占世界状态。
- `Room/`、`Session/`、`Entity/`：房间、会话与实体存储。
- `Aoi/`：AOI 网格、分带广播、bot 压测。
- `Handler/`：登录、位置同步、施法、时钟同步、echo。
- `Monster/`：服务端权威怪物（行为树）。
- `Movement/`：ORCA 避让。
- [Navigation](Navigation/README.md)：三角面 navmesh（A* + 漏斗）、navbin 加载、每 tick 查询预算。
- `Config/`：`monster_config.json` 等。
- `Message/`、`Util/`：消息与数学工具。
- 共享规则：`Assets/Scripts/Hotfix/GameSystems/SyncCore/`；共享协议：`Assets/Scripts/AOT/Protocol/`。

## 多人战斗一致性

- **权威复活落点**：`Config/maps.json` 的 `playerSpawn` 提供地图复活坐标，默认地图为 DemoDay。
  支持该能力的玩家到点复活时，主循环同时更新 HP、位置、速度、防御和移动校验基准，
  再发送 `RespawnNotice`。位置同步丢弃死亡期间和复活 tick 不匹配的请求，
  首个新请求按一个上报周期的移动预算校验。
- **DamageResult 观察者扩播**：`CastHandler.CollectDamageRecipients` 的收件人 =
  施法者 + 每个受击者 + 施法者的 AOI 观察者 + 每个受击者的 AOI 观察者（`HashSet` 去重）。
  因此旁观者与施法者/受击者收到**同一条** `DamageResult`（同一掷骰、同一余血），
  不会出现「只有当事人看到伤害」的不一致。怪物施法者无 session，`GetSession` 自然跳过。
- **登录名册**：`LoginHandler.BuildRoster` 把房间内已有玩家以 `PlayerEnterRoom` 发给新登录者，
  并向其他人广播新玩家；断线发 `PlayerLeaveRoom`。客户端 `PlayerNameRegistry` 主线程落库，
  远程名牌据此显示玩家名（缺失回退 `玩家 {id}`）。
- **权威 vitals 进快照**：`EntitySnapshot` 携带 Hp/MaxHp/IsDefending（掩码位
  `Health`/`Defending`）。`Dead`/`Defending` **每快照携带**（D-N2：状态不是事件；保住旧客户端
  无条件读 `Dead` 的语义），`Health` 是条件字段（Baseline / 血量翻转）；AOI 分带调度把血量/
  防御变化视为脏位立即推送。客户端据此更新远程 `Targetable` 与名牌血条。
  `Server.Tests/Aoi/BandwidthBudgetTests` 断言含新字段的全字段快照字节上限。

## 属性剑气 v1（2026-11-14，Plan 01–04）

- **共享目录**：启动从 `Config/elemental_catalog.json` 加载、`ElementalCatalogValidator` 校验并
  计算 SHA-256 指纹；缺表/校验失败时停用该功能（登录不发放武器）。客户端登录带指纹与
  `ProtocolFeatures.ElementalSwordQiV1`，不匹配则拒绝登录。
- **权威换装**：`LoginHandler` 按目录初始清单发放会话内武器实例并装备默认剑；
  `EquipHandler` 校验归属/版本、按 `ClientEquipSeq` 幂等，确认/拒绝都带当前装备摘要。
  `PlayerState` 持有 `WeaponInstanceTemplates / EquipmentRevision / LifeId`。
- **施法身份**：`CombatResolver.Decide` 用世界级单调 `ulong CastId`（玩家与 NPC 共用），
  冻结 `WeaponCastSnapshot`（攻击力/攻速/元素/装备版本），校验 `ClientCastSeq /
  EquipmentRevision / CasterLifeId`；重复序号只回原 `CastConfirm`，不重排判定。
  普攻首判/接续/冷却按共享时序与攻速换算 tick，`NextAllowedAttackTick` 在施法时固定。
- **伤害**：普攻基础伤害乘武器 `BaseAttack / 默认武器 BaseAttack`；剑气分支另乘共享
  `SwordQiDamageCoefficient` 与武器 `SwordQiDamageMultiplier`。挥击与剑气各自独立结算。
- **发射/停点**：首个 LineAll 段发布 `ProjectileLaunchBroadcast`（与判定同一 XZ 原点/朝向，
  12m / 18m/s / 3 段）；格挡、死亡/离场发布 `ProjectileStopBroadcast`，按 `CastId`
  只停指定一发。发射收件人记录在 `CastHandler`，终止时并上已通知会话。
- **场景隔离**：候选、挥砍/发射广播与伤害收件人统一按 `PlayerState.ZoneId` 过滤；
  跨场景同坐标不互相命中。怪物按地图实例绑定场景仍未实现（已知限制）。
- **NpcAction 广播（Plan 02 Part A）**：`NpcActionEvent`（2004）随技能/受击/死亡阶段变化发送；
  AOI 进入基线带 `NpcAction` 与 `EntityFieldMask.NpcAction`；事件与广播一样按 `ZoneId` 过滤。
- **AOI 发送原因与条件字段（Plan 02 Part B）**：`AoiBandScheduler.ShouldSend` 返回
  `Baseline / Changed / Keepalive`。首帧与离开后重见为 `Baseline`（带 Health 与
  NpcAction/Combat 投影）；字段翻转立即 `Changed`（Health 只在翻转时置位并重置节拍）；
  静止实体每 4 tick `Keepalive`（只带位置/身份基础字段）。**Dead/Defending 每帧携带**
  （D-N2 状态语义），只有 Health 是条件字段，客户端按位沿用 last-known。
- **战斗投影（Plan 02 Part B）**：AOI 基线快照带 `EntityFieldMask.Combat` 与完整
  `CombatProjection`（`CombatProjectionBuilder`：HP/装备/活动施法；Plan 04 起
  `Statuses` 由 `ServerStatusManager.Runtime.Active` 按组映射、`Control` 取权威
  `ControlSpeedScale/ControlActionLock/ControlVersion`；`CombatVersion/LifeId`
  同基线下发）。`PlayerState.CombatVersion` 在换装、受伤/
  死亡/复活、防御姿态变化与活动施法阶段推进时递增并置 `CombatDirty`；
  `Server3C.SendCombatStateUpdates` 每 tick 只对 dirty 实体发送 `CombatStateUpdate`（318），
  收件人 = 本人 + 同场景观察者（NPC 为观察者）；场景过滤统一走 `IsSameCombatScene`
  （与 NpcAction 一致，空 `ZoneId` = 广播），不再用严格 `ZoneId != scene`。
  活动施法：`Decide` 成功置 `Windup`（`EndTick` = 最后一段判定 tick），判定期推进
  `Active`/`Recovery`，段结束/取消/死亡清空。
- **晚进观察者与带宽（Plan 02 Part B）**：`AoiBroadcaster.OnBaselineEntity` 只在基线触发，
  `CastHandler.ResendActiveProjectiles` 把同 `CombatSceneId` 的在途剑气原样补发（保留原始
  `LaunchTick/ServerTimestamp`，客户端据此推导飞行进度）；`PruneExpiredLaunches` 按飞行时长
  清理过期条目。`GlobalGameSettings.BandwidthBudgetBytesPerSec = 100_000`（≈100 KB/s），
  `BandwidthBudgetTests` 用 100 实体 × 15Hz 的快照估算锁定上限。
- **接收 tick 位移预算（Plan 03）**：登录响应始终下发 `ProtocolFeatures.PvpMovementBudgetV1`；
  客户端在 `LoginRequest.SupportedFeatures` 携带该位时 `PlayerState.StrictMovementBudget = true`，
  `PositionSyncHandler.ValidateWithBudget` 生效。预算只看**服务端接收 tick**
  （`MovementBudgetMath.ClampDeltaSeconds`，15Hz，单次上限 0.5s），同一 tick 的第二包额度为 0；
  普通移动 = `Walk 5 / Sprint 8 × 防御 0.4 × clamp(StatusScale,0,4) × dt`，
  `ControlActionLock` 时为 0。`ClientControlVersion < player.ControlVersion` 的包直接纠偏且
  不消耗预算，`LifeId` 不匹配同样纠偏。未携带能力位的旧客户端继续走原 `MovementValidator`
  （不按接收 tick 收紧）。
- **突进 `DisplacementWindow`**：`CastHandler.Decide` 对共享表 `Displacements` 命中的技能建立
  窗口：`CastId`、`SkillId`、`StartTick` = 接受到达 tick、
  `EndTick = StartTick + SecondsToTicks(DashDuration × 4)`
  （`SecondsToTicks = ⌈秒 × TickRateHz⌉`，15Hz 下 20001 的窗口为 18 ticks；窗口按 4× 时长
  覆盖减速把同一段距离拉长）、`TotalDistance` = 表值、
  `Speed = distance / (duration − 起手 − 收尾)`。位置同步必须同时匹配 `CastId` 与
  `DisplacementSkillId` 且 `StartTick ≤ nowTick < EndTick` 才发放；额度 =
  `min(Speed × clamp(StatusScale,0,4) × dt, TotalDistance + Speed×1tick − GrantedDistance)`，
  重发不能叠加。**窗口寿命**：施法判定正常完成**不清**窗口；窗口在 `EndTick`（`GrantDash`
  首次看到 `nowTick >= EndTick` 时清 `player.Displacement`）或取消/死亡/复活/新位移施法时结束。
  `EndTick = tick + SecondsToTicks(DashDuration × 4)`（15Hz 下 20001 = 18 ticks）。
- **跳跃轨迹**：`JumpPhase.Start` 以 `JumpSeq` 去重，动作锁时拒绝新起跳，`End` 结束。服务端
  记录起跳接收 tick 与起跳水平速度（按 `SprintSpeed = 8` 封顶）。普通空中移动由 normal 预算
  覆盖，只有动作锁的空中段额外发 `JumpInertiaDistance`（总上限
  `JumpHorizontalSpeed × 2 × MaxRiseSeconds`），不双算。有导航时 Y 轴按共享
  `JumpForce/Gravity/MaxRiseSeconds` 的抛物线 + 0.05m 容差封顶，并夹在地面 −1m 以上。
- **PvP 基线**：元素会话登录时 `SetMaxHp(720)`（`Pvp.PlayerMaxHp`），旧客户端保持 100 HP；
  NPC → 已登录玩家的**直接伤害**按 `target.MaxHp / CombatRules.PveDamageBaselineHp`（基准 100）
  缩放——720 HP 会话 ×7.2，保持原 PvE TTK；玩家/bot 来源与打怪不缩放。
- **控制注入入口**：`Server3C.ApplyPlayerControl(playerId, speedScale, actionLock)` 写权威控制
  状态，变化时递增 `ControlVersion` 并 bump `CombatVersion`（触发 318）；首次 `ActionLock` 取消
  施法者未提交段（已发射剑气照飞）；NPC 首次 `ActionLock` 走
  `MonsterModule.CancelAction`（霸体免打断）。动作锁期间 `CastHandler.Decide` 以
  `control-lock` 拒绝新施法，位置同步不发普通移动预算。**Plan 04 起生产调用者 =
  `ServerStatusManager` 的状态运行时**（冰/雷变化经 `Server3C.RegisterEntity` 注入的
  control sink 调用）；不要在别处直接写 `ControlSpeedScale/ControlActionLock`。
- **状态运行时（Plan 04）**：`PlayerState.ConfigureStatuses(catalog, sink)` 为每个权威实体
  建 `ServerStatusManager`（薄包装 `SyncCore.StatusRuntime`）；应用/刷新/到期/DoT 末跳都会
  `SyncControl` + `BumpCombatVersion`，因此 `CombatStateUpdate` 的 `Statuses[]` 与
  `ControlSummary.SpeedScale/ActionLock` 随冰/雷实际变化。
- **同 tick 顺序（Plan 04）**：`GameLoop` 在消费网络包**之前**跑 pre-tick：
  解除到期修饰（风先失效）→ `StatusTickService` 结算到期灼烧跳（`DamageResult`，
  `DamageKind=Dot`、`EventSeq/EventId/CombatVersion` 逐事件填充）→ 推进/移除 DoT →
  再处理本 tick 输入与命中段。WS 会话的输入用 `GameLoop.EnqueueAfterPreTick` 排到 pre-tick
  之后；KCP 队列保持原有顺序。
- **DoT 与仇恨**：灼烧跳走 `StatusTickService`，收件人 = 来源 + 目标 + 双方观察者
  （`CastHandler.BroadcastDotDamageAsync`）；PvE 读取风易伤（PvP 不放大灼烧）；
  怪物每跳按 `damage × ThreatPerDamage` 计仇恨、不叠加 `ThreatFlat`；死亡立即清状态并
  拒绝后续跳伤害。玩家 `MarkDead`/`TryRespawn` 与怪物 `ArriveHome`（Evade 到家）清状态。
- **首个有效命中附加元素（Plan 04）**：只有玩家 `10002` 且武器 `ElementId > 0`；正伤害、
  未格挡、目标存活才附加，按 `(TargetId, LifeId)` 每次施法只附加一次（挥砍先命中则剑气不再
  附加，剑气先命中则由剑气附加）。风易伤在扣血前读取——本次命中不吃刚附加的倍率，
  同次施法的后续命中才生效；PvE 用状态表 ×2.0，PvP 用共享
  `Pvp.WindVulnerabilityMultiplier = 1.28`（PvP 不放大灼烧）。

状态：Plan 01–05 已完成（Part A 取消/NpcAction、Part B 战斗投影/带宽、Plan 03 位移/跳跃预算与
PvP 基线、Plan 04 状态结算/DoT/元素表现、Plan 05 AOI 反查索引/三客户端/带宽/注入验收），
服务端全量 **667/667**。Plan 05 未新增战斗机制或协议字段（`ProtocolVersion` 保持 1）。
未启用：**离线单机共享状态运行时**（本地 `StatusEffectManager` 的冰冻/灼烧保持独立，
四元素运行时留给 Plan 06/backlog）、Addressables 元素资源与远端武器模型（Plan 06/backlog）。

Plan 03 验收证据（可证伪）：

- 预算：`PvpMovementBudgetTests` 覆盖同 tick 第二包零额度、冲刺×防御×状态合成、
  旧控制版本拒绝、突进 CastId 匹配。
- 跳跃：`JumpTrajectoryBudgetTests` 覆盖麻痹起跳拒绝、空中惯性按起跳水平速度发放。
- PvP：`LoginCombatBaselineTests` 断言 720 HP；`PveDamageScaleTests` 断言 NPC→720 HP
  玩家伤害 ×7.2，保持原 PvE TTK。

Plan 04 验收证据（服务端，全部在 654/654 内）：

- 状态运行时：`StatusRuntimeTests`（11 项）锁定刷新/末跳/控制派生/PvP 免疫与 4s 不刷新；
  `ServerStatusManagerTests`（9 项）锁定控制 sink 与投影版本；`GameLoopOrderingTests`
  锁定 pre-tick 顺序与 WS 输入顺序。
- 元素附加：`ElementalApplicationTests`（9 项）锁定同次施法每目标一次、挥砍/剑气先后、
  格挡与死亡不附加、PvE/PvP 风倍率；`EventIdentityTests`（2 项）锁定事件序号。
- 取消重入：`CastCancelDuringDrainTests`（2 项）锁定状态附加 → 控制 sink → `CancelCaster`
  在 `DrainResolved` 压缩途中重入不越界、不覆盖/丢弃后续待判定施法（C1）。
- DoT：`DotDamageTests`（9 项）与 `DotTerminalTickTests`（4 项）锁定每跳伤害/仇恨、
  `DamageKind=Dot`、末跳只结算一次、到期/死亡清理。
- 怪物控制：`MonsterStatusControlTests`（**8 项**）锁定减速对真实位移的缩放（无邻居期望位移
  逐字验证；`Slow_Scales_Orca_MaxStep_With_Monster_Neighbor_And_Keeps_Avoidance_Side`
  用正前方 1.8m 静止怪物证明 ORCA `maxStep` 同缩——`SpeedScale=0.7` 时解恰好截断在
  `moveSpeed×0.7×dt` 的 maxStep 圆上、未缩放对照为 `moveSpeed×dt`，慢/快步长比 0.7，
  避让横向分量不因缩放反转）、麻痹打断与霸体；`MonsterEvadeTests` 覆盖 Evade 仇恨清空/回巢，
  状态清除由 `MonsterStatusControlTests` / `DotDamageTests` 覆盖。

Plan 05 验收证据（2026-11-20；服务端项均在 667/667 内，Unity 结果另列）：

- AOI 反查：`AoiReverseIndexTests`（9 项）锁定索引与暴力反查逐项相等、80/90m 迟滞、
  清理/重登不残留、格边界与权威传送陈旧边、N=1000 单次反查 ≤1ms（Debug 本机中位数）。
- 带宽：`BandwidthBudgetTests` 100 实体 ×15Hz×60s 常规回放平均 **53.4 KB/s**
  （3,204,397 B/60s，单 tick 峰值 3,980 B，投影基线 227 B）；100 实体同时 DoT 最坏变体
  **76.9 KB/s**（4,615,939 B/60s，单 tick 峰值 27,542 B）。预算
  `BandwidthBudgetBytesPerSec = 100_000` 未上调，并有状态/DoT 流下限防静默删除。
- 移动预算：`PvpMovementBudgetTests` 9/9，其中本次新增 2 项：空中麻痹仅发放跳跃惯性
  （6 m/s × 1.0s = 6.0m）与无跳跃 `ActionLock` 普通预算为 0。
- Unity（另列，不计入 667/667）：三客户端 PlayMode（真实 `Server3C:8000`）
  `ThreeClientElementalAcceptanceTests` 1/1、`ThreeClientCombatConsistencyTests` 1/1；
  客户端收包注入目标类合计 72/72。
- 已知失败（非 Plan 05 回归）：Unity `RemoteEntityRegistryTests` 16/17，唯一失败为
  既有的 `Bot_Radius` PPtr 环境失败；`MonsterWorldTests` 导航模式 Slime 巡逻卡死为
  服务端导航债；`PlayerJumpBodyCollisionTests` 在 batchmode 0/3（夹具自身 PPtr 伪报 +
  固定 180 帧落地等待不足）。backlog：`_pushVector` 对远程 `NpcBody` 不生效（`IDamageable`
  门槛，见路线图 Plan 06 登记）。

`CastCancelBroadcast`（317）、`CombatStateUpdate`（318）与 `NpcActionEvent`（2004，含 `LifeId`）
已启用；取消/动作事件/投影统一按 `IsSameCombatScene`（相对 `ZoneId`，空值广播）过滤。

## 投射物与落点判定（20003 火球）

- 20003 在共享表 `CombatRules` 中是 `ProjectileKind.CircleAtPoint`：`radius=1.5`、
  `MaxImpactDistance=15`、`speed=18`，与客户端 `Fireball_SkillQ.asset` 逐值一致
  （EditMode `FireballAssetTableTests` 锁定）。
- `CastHandler.Decide` 校验落点有限且距施法者 ≤ `MaxImpactDistance`（否则 `impact-not-finite` /
  `impact-too-far`），冷却在**决策**时记录（空挥也冷却）。
- 判定 tick = `tick + HitDelayTicks + ProjectileTravelTicks`；`ProjectileTravelTicks` 按
  `ceil(距离 / speed × 15Hz)` 换算飞行延迟；非 `CircleAtPoint`、速度缺失或落点非有限时为 0。
- `ResolveStage` 在 `ImpactX/Y/Z` 处以 `HitResolver.ResolveCircle` 结算；落点即客户端松手上报的
  `ImpactX/Y/Z`，因此联机下客户端落点圈与服务端受击位置一致。

## 旋转多段判定（20002，2026-11-10）

- `CombatRules` 20002 带 `RepeatSpec`：首判 = 起手剪辑 0.2s + 0.2s，之后每 0.2s 一段，
  `MaxDuration` 3s，每目标最多 5 段（与客户端 `Spin_SkillR` 资产逐值一致，`SpinAssetTableTests` 锁定）。
- `CombatResolver.ResolveStage` 每段按当时的施法者位置与存活目标重新判定 2.5m 圆；
  `RepeatHitCounts` 每目标独立计数，**不走** `GroupHitIds` 的每施法一次去重。
- 施法者死亡/移除，或收到 `CancelCastRequest`，丢弃未到期段；复活后不会继续结算旧旋转。

## 剑气回荡（20006，2026-11-18）

- `CombatResolver.Decide` 对 20006 走专用分支：跳过客户端形状校验（客户端发零形状），按
  `SwordQiEchoRules.Spec` 选最近 8 个同区目标；空名单返回 `no-target` 且不记冷却。接受时
  在决策 tick 记录 12s 冷却、提交 `[StartTick, EndTick)` 无敌窗口，并锁定目标的
  `PlayerState` 对象身份与 `LifeId`。
- 首次接触时间表由 `SwordQiEchoRules.BuildContacts` 生成；`DrainResolved` 先处理普通
  pending，再按固定顺序处理 Echo。到点重查存活、同区、圆心/半径与生命代次；失效跳过、
  不补排期。即使全部目标失效，施法与无敌仍持续到原定 `EndTick`。
- 伤害走与普通 PvE/PvP 共用的结算入口（无敌检查在掷骰/格挡/扣血之前）；Echo 的格挡方向
  固定使用接受施法时的圆心，不随施法者移动。NPC 受击仍走 `NpcDamaged` 仇恨回调。
- 请求按玩家缓存最近 `EchoRequestId`：重复序号重放决定，较旧序号回 `stale-request` 且不改状态；
  已结束时重放 `Ended`。断线/离场经 `Server3C.UnregisterEntity → CastHandler.OnPlayerRemoved`
  清除实例与缓存。
- 登录握手把 `SupportedFeatures` 写入权威 `PlayerState.Features`，`LoginResponse.EnabledFeatures`
  回显协商结果；`SwordQiEchoState` 只发给同区且已协商 `ProtocolFeatures.SwordQiEcho` 的收件人。
  `EntityStore.AddNpc` 将 NPC `ZoneId` 固定为 `Global`。
- 测试：`Server.Tests/Combat/SwordQiEchoCombatTests`（无敌边界、换命、格挡方向、区域/能力过滤）、
  `Server.Tests/Protocol/SwordQiEchoProtocolTests`（往返、tag 钉死、旧字段缺省）。

## 攻击高度与跳跃规避（B1，2026-11-10）

- `CombatRules` 的地面近战（10001/10002 挥击、50001–50005）带 `AttackHeightSpec [-0.3, 1.2]`；
  `CombatCandidate.Height` 提供目标受击高度（玩家 1.8m、NPC = max(1m, bodyRadius×2)）。
- `ResolveSwing` 走高度重叠；剑气/火球/AOE 分支不传高度，保持纯 XZ。跳跃到 2m 时脱离地面近战体积。
- 玩家 Y 以客户端上报为候选；有 navmesh 时 `ResolveCombatHeight` 钳到 `[地面−1m, 地面+MaxJumpHeight]`
  （`Server3C.EnableMonsters` 接线 `GroundHeight`）。无导航宿主沿用客户端 Y。
  **信任面**：限幅只挡离谱高度，挡不住客户端始终上报可达跳高——与既有客户端权威位置模型一致，登记为方案 A 风险。

## 子模块 README

- [Monster](Monster/README.md) —— 行为树与权威怪物
- [Movement](Movement/README.md) —— ORCA 避让

## 测试

`Server/Server.Tests/`（xUnit）：AOI、战斗裁决、怪物、移动、协议、会话等。

```bash
# 官方 testhost（会拉起 VSTest testhost 子进程）
bash tools/test-server.sh
bash tools/test-server.sh --filter "FullyQualifiedName~Orca"

# 本机 workaround：testhost 以 Win32Exception(5) Access denied 退出时用 in-process runner
bash tools/test-server-inprocess.sh
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Navigation"
CONFIG=Release bash tools/test-server-inprocess.sh   # benchmark 数字用 Release
```

**为什么有第二个脚本**：本机（Windows 10 / .NET SDK 10）`dotnet test` 的 testhost 在
`Process.EnableRaisingEvents` 上抛 `Win32Exception(5): 拒绝访问`，测试一个都不跑。
`tools/ServerTestRunner` 用 xunit 自带的 `AssemblyRunner` 在**同一进程**里发现/执行
`Server.Tests.dll`（不经过 VSTest），输出与 `dotnet test` 等价的 PASS/FAIL 摘要。
它引用 `Server.Tests.csproj`，所以 `dotnet build` + runner 会复制全部依赖；
干净克隆可直接复现 321/321（修复轮后 348/348；P2 后 395/395）。脚本把 `TEMP` 指到仓库内
`.tmp/server-tests`，避免受限环境阻止测试写 `Path.GetTempPath()`。
