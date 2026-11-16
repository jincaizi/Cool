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

## 属性剑气 v1（2026-11-14，Plan 01–02）

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
  `CombatProjection`（`CombatProjectionBuilder`：HP/装备/活动施法；`Statuses` 暂空、`Control`
  为默认值，`CombatVersion/LifeId` 同基线下发）。`PlayerState.CombatVersion` 在换装、受伤/
  死亡/复活、防御姿态变化与活动施法阶段推进时递增并置 `CombatDirty`；
  `Server3C.SendCombatStateUpdates` 每 tick 只对 dirty 实体发送 `CombatStateUpdate`（311），
  收件人 = 本人 + 同场景观察者（NPC 为观察者）；场景过滤统一走 `IsSameCombatScene`
  （与 NpcAction 一致，空 `ZoneId` = 广播），不再用严格 `ZoneId != scene`。
  活动施法：`Decide` 成功置 `Windup`（`EndTick` = 最后一段判定 tick），判定期推进
  `Active`/`Recovery`，段结束/取消/死亡清空。
- **晚进观察者与带宽（Plan 02 Part B）**：`AoiBroadcaster.OnBaselineEntity` 只在基线触发，
  `CastHandler.ResendActiveProjectiles` 把同 `CombatSceneId` 的在途剑气原样补发（保留原始
  `LaunchTick/ServerTimestamp`，客户端据此推导飞行进度）；`PruneExpiredLaunches` 按飞行时长
  清理过期条目。`GlobalGameSettings.BandwidthBudgetBytesPerSec = 100_000`（≈100 KB/s），
  `BandwidthBudgetTests` 用 100 实体 × 15Hz 的快照估算锁定上限。

状态：Plan 01 与 Plan 02 已完成（Part A 取消/NpcAction、Part B 战斗投影/带宽），
服务端全量 554/554；Plan 03–05 未开始。
未启用：元素状态结算/DoT 与 `Statuses[]` 内容（Plan 04）、`ControlSummary` 的实际控制与
PvP 控制预算（Plan 03）、元素 VFX 与远端武器模型（Plan 04/05）。

`CastCancelBroadcast`（310）、`CombatStateUpdate`（311）与 `NpcActionEvent`（2004，含 `LifeId`）
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
