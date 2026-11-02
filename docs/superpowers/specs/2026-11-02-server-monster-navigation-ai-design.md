# 服务端怪物寻路与 AI 行为重构设计

日期：2026-11-02
状态：已评审，待实现
关联模块：`Server/Monster`、`Server/Navigation`、`Server/Movement`、`SyncLayer`、`SyncCore`、`Protocol`

## 1. 背景与现状

### 1.1 需求

当前怪物 AI 在联网模式下已经由服务端 `Server/Monster` 驱动，但服务端移动只是朝目标直线走 + ORCA 避让，没有静态地形寻路；客户端 `MonsterMovement` 使用 `NavMeshAgent`，却只在单机模式运行。这不符合 MMO 的服务端权威设计，也导致两套 AI 逻辑长期漂移。

本次重构的目标是：

1. 服务端拥有怪物寻路的唯一权威，能绕开岩石、树木、湖岸等静态地形；
2. 服务端 AI 行为升级为 MMO 语义：仇恨表与目标切换、多技能选择、脱战回巢与重置、玩家对怪物的控制/击退/状态；
3. 删除客户端单机怪物 AI，客户端只保留表现、配置与伤害接口；
4. 导航数据按瓦片组织，为以后扩展大地图保留接口，但本期只做单张 DemoDay。

### 1.2 已确认的现状

- 联网模式：服务端 `Server/Monster/MonsterModule.cs` 每 15Hz tick 做感知、行为树（巡逻/追击/攻击/返回）、施法入队、死亡与重生；客户端只接收快照。
- 客户端 `Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIBrain.cs` 那套 FSM 只在单机模式运行；`ClientNetworkHost` 联网时会禁用全部 `MonsterSpawner`。
- 客户端 `MonsterMovement` 使用 `NavMeshAgent`；服务端 `SimpleMoveSystem` 没有静态障碍寻路。
- 服务端行为比客户端少：没有 Defend/Taunt/击退/多技能/仇恨表；P5 指南把 AggroTable 列为 backlog。
- 服务端目前没有状态效果，也没有伤害来源归属：`PlayerState.ApplyDamage` 只扣血。
- 客户端冰冻/灼烧设计明确“只在施加者本地表现、不同步”；客户端已有 `MonsterDotDamageEvent`，专用于 DoT 飘字，不触发顿帧/震屏/命中粒子。
- DemoDay 已烘好 NavMesh：
  - `Assets/SimpleLowPolyNature/Scenes/DemoDay/NavMesh-LowpolyTerrain.asset`（约 157KB）；
  - 项目只有一个 Agent 类型 `Humanoid`，`agentRadius=0.5`、`agentHeight=1.6`、`agentSlope=45`、`agentClimb=0.75`（`ProjectSettings/NavMeshAreas.asset`）；
  - 场景只有一个启用的 `NavMeshSurface`，`CollectObjects=All`、`UseGeometry=RenderMeshes`、LayerMask 全开；
  - 场景没有 `NavMeshObstacle` 组件，也没有 `NavMeshLink`/`OffMeshLink`。
- 怪物 `bodyRadius` 最大为 SpiderBoss 的 `1.74`，DogKnight 为 `0.88`（`Server/Config/monster_config.json`）。0.5m 烘出的 navmesh 不能直接保证大怪不穿模。

## 2. 目标与非目标

### 2.1 目标

- 服务端寻路：单张 DemoDay 上 100–200 只怪能绕静态地形追击/巡逻/回巢，不穿模、不卡死。
- 服务端行为：仇恨表、目标切换、多技能、脱战回巢与重置、玩家→怪物的控制/击退/状态。
- 客户端表现：多技能动画/特效、状态染色与冻结表现、DoT 飘字；不参与任何 AI 或命中裁决。
- 删除单机怪物 AI，消除两套逻辑。
- 所有新逻辑可单测；Unity 侧有导出新鲜度与表现契约测试。

### 2.2 非目标（本期不做）

- 无缝大世界流式导航（只保留瓦片格式与 `EnsureTiles(bounds)` 入口）。
- 动态障碍重烘焙、`NavMeshObstacle`/`NavMeshLink` 支持。
- 多楼层/桥/重叠 navmesh 的精确投影（v1 假设单层地形）。
- 玩家移动权威化、玩家输入锁、怪物对玩家施加控制。
- 玩家投射物/攻击的静态遮挡（既有问题，不在本次范围）。
- 路径缓存/LRU、分层导航、跨进程导航服务。
- 单机模式保留。

## 3. 总体架构

### 3.1 服务端模块

```
Server/Navigation/                      新增，纯 .NET 8，无 Unity 依赖
  NavDataFormat.cs                      二进制瓦片读取/校验
  NavMeshGraph.cs                       三角面邻接、边界边、空间哈希、clearance
  NavMeshPathfinder.cs                  最近三角面投影 + A* + 漏斗 + 路径校验
  NavPath.cs                            路点、长度、失败原因
  NavigationService.cs                  地图注册、查询预算、未来流式加载入口

Server/Monster/
  MonsterModule.cs                      只做 tick 编排
  MonsterState.cs                       黑板：mode/target/threat/path/status
  Brain/
    MonsterMode.cs                      Patrol / Combat / Evade / Dead
    MonsterBrain.cs                     模式转移
    ThreatTable.cs                      威胁值、排序、清除
    TargetSelector.cs                   感知 + 仇恨选目标
    SkillSelector.cs                    多技能过滤/优先级
    LeashController.cs                  脱战触发、回巢、重置
  Combat/
    MonsterCastBuilder.cs               从 SkillEntry 构建 CastRequest/Shape
  Status/
    MonsterStatusController.cs          控制/减速/灼烧/击退
  Movement/
    PathFollower.cs                     路点跟随 → 期望方向
    SimpleMoveSystem.cs                 期望方向 + ORCA → 实际位移

Server/Handler/CastHandler.cs           伤害回调、状态施加、判定帧 LOS/控制校验
Server/Movement/AvoidanceSystem.cs      邻居速度改为实际位移
Server/Config/
  monster_config.json                   模板 + 技能表 + 仇恨/脱战/控制参数
  maps.json                             地图 id → nav 文件
  nav/demoday.navbin                    导出的导航数据
  status_effects.json                   skillId → 状态效果
```

### 3.2 每 tick 数据流

1. `AvoidanceSystem.EnsureSnapshot(tick)`：按实际上一 tick 位移重建邻居速度。
2. `MonsterModule.Tick(tick)` 逐怪：
   状态 tick → 死亡检查 → 控制锁 → 仇恨/目标有效性 → 模式转移 → 模式行为（巡逻/战斗/脱战）→ 位置/动作 flush。
3. `DrainPendingCasts()` → `CastHandler.Decide`/`BroadcastSwingAsync`（沿用现有链路）。
4. `DrainResolved()` → `CastHandler` 判定帧：
   - 对 NPC caster 检查眩晕/控制与 LOS；
   - `ApplyHit` 扣血，回调 `MonsterModule.OnDamageTaken(npcId, attackerId, damage, DamageKind)`；
   - 玩家技能命中怪物时施加状态。
5. `MonsterModule.DrainSyntheticDamage()` → 灼烧等 DoT 的 `DamageResult` 广播。
6. AOI 快照广播；状态位变化作为脏位立即推送。
7. 死亡计时/复活。

### 3.3 分期

| 期 | 内容 | 验收 |
|---|---|---|
| P1 | 导航导出 + 图/A*/漏斗 + 路点跟随 + 巡逻/追击/回巢接入 + 最小目标保持（P2 由 ThreatTable 替换） | 怪物绕岩石/湖岸追击，不穿模、不卡死；无导航时旧测试全绿 |
| P2 | 顶层模式机 + 仇恨表 + 多技能 + 脱战回巢 | 目标切换、技能选择、脱战重置可单测/可观察 |
| P3（拆分 3a/3b/3c） | 玩家→怪物的控制/击退/状态 + 快照状态位 + 客户端表现 | 冰冻/眩晕/灼烧/击退服务端裁决、客户端可见 |
| P3a | Stun/Freeze/Slow + 快照状态位 | 最小控制闭环 |
| P3b | Knockback | 冲量位移与投影 |
| P3c | Burn DoT + 合成伤害广播 | 飘字、无顿帧、不触发受击硬直 |
| P4 | 删除客户端单机 AI/Spawner/本地怪物运行时 + 文档收尾 | 联网无回归，场景/预制体无 Missing Script |

每期独立可验收，不做一次性大合并。

## 4. 导航子系统

### 4.1 导出管线

Editor-only 导出器：`Assets/Scripts/Editor/Navigation/NavMeshExporter.cs`。

流程：

1. 只打开 DemoDay 单场景；校验至少一个启用的 `NavMeshSurface`，且所有 surface 的 agentTypeID 都是 0；拒绝其他 agent 类型、拒绝 `NavMeshLink`/`OffMeshLink`。
2. `NavMesh.CalculateTriangulation()` 取顶点/索引/area。`NavMeshSurface` 是 `[ExecuteAlways]`，`OnEnable` 会 `AddData()`，EditMode 下应能取到烘焙数据；导出器仍要断言三角面数 > 0，数据为空时**报错**，不要静默调 `BuildNavMesh()`（会改资产）。
3. 规范化：
   - 坐标量化到 1mm 后去重顶点；
   - 丢弃退化三角形；
   - 按稳定键排序顶点与三角面；
   - **归一化 winding**：XZ 有向面积统一为正，漏斗 portal 的左右顺序才有稳定约定；
   - 校验每个内部边恰好 2 个三角面；free 边统计写日志。
4. 写出 `Server/Config/nav/demoday.navbin`，文件头写 `sourceHash`（navmesh 资产 + 场景文件）与 `canonicalHash`（规范数据）。
5. 另提供 `Tools/Navigation/Rebake and Export` 菜单，显式重建 `NavMeshSurface` 后再导出；普通导出只使用现有烘焙数据。

批量模式：`-executeMethod NavMeshExporter.ExportDemoDay`。

**新鲜度守卫**：EditMode `NavMeshExportFreshnessTests` 重新从场景 navmesh 计算 `canonicalHash`，与已提交文件比较；不一致直接红。场景文件 hash 变化但 navmesh 未变时给 warning，不硬红（避免灯光等无关改动噪音），提示检查是否需要重烘。

### 4.2 二进制格式

`NAV1`，全部小端：

```
header:
  magic "NAV1"
  version u16
  mapId (u16 length + UTF8)
  sourceHash u64
  canonicalHash u64
  bounds minX/minZ/maxX/maxZ f32
  tileSize f32
  tileCount u16
  areaCount u8
  areaCosts f32[areaCount]
  vertexCount u32
  vertices: f32 x,y,z ×vertexCount   // 全局顶点表；y 保留给未来多层地形/投影 tie-break

tile:
  tileX i16
  tileZ i16
  triangleCount u32
  indices: u32 ×3 ×triangleCount     // 全局顶点索引
  areas:   u8 ×triangleCount
```

- 瓦片按 64m XZ 划分；三角面按质心归属；**顶点索引全局唯一**，跨瓦片共享边在运行时自然连通。
- v1 启动时加载整张地图全部瓦片；`NavigationService` 预留 `EnsureTiles(bounds)` 钩子。
- area cost 来自 `NavMesh.GetAreaCost(areaIndex)`；`Jump` 等区域用真实 cost，不把非 Walkable 当 Walkable。

### 4.3 运行时图与 A*

`NavMeshGraph`：

- 读取/校验 header、瓦片、hash；拒绝截断、版本不符、空数据。
- 构建三角面邻接：共享边按全局顶点索引配对；每个内部边必须恰好 2 个三角面。
- 构建**边界边**（只被一个三角面共享的边）与边界边空间网格。
- 计算每个三角面的 `clearance`：质心/边中点到最近边界边的最短 XZ 距离。**在服务端加载期计算**，不写进文件；导出器与服务端不重复实现该算法。
- 构建三角面 AABB 空间网格（按 AABB 插入，不按质心，跨瓦片三角形才能被邻格查询）。
- `FindNearestTriangle(x,z)`：点内测试；失败时查最近三角面/边，返回投影距离。

`NavMeshPathfinder`：

- 节点 = 三角面；邻居 = 共享边相邻三角面；代价 = 质心 XZ 距离；启发 = 质心到目标三角面质心的 XZ 距离。
- **确定性**：f 值相同按三角面索引排序；邻居按索引顺序枚举；同输入必须返回同一条路径。
- **clearance**：
  - 进入某三角面时，若 `clearance < bodyRadius * 0.9` 则拒绝（起点/终点三角面豁免）；
  - 若 `clearance < bodyRadius * 1.1` 则加代价，避免贴边；
  - `bodyRadius` 取模板配置，解决 0.5m navmesh 与大怪半径不匹配的问题。
- 起点/终点投影到最近三角面；投影距离 > `ProjectionRadius`（默认 2m）返回 `start-off-navmesh`/`goal-off-navmesh`。
- 漏斗（simple stupid funnel）输出路点；去掉重复/共线点。
- 路径校验：每 0.5m 采样，所有点必须在可通行三角面内且精确到边界边的距离 ≥ `bodyRadius * 1.1`；起点/终点 1m 内放宽。失败时用更保守的有效半径重试一次，仍失败返回 `blocked`。
- `MaxExpandedNodes`（默认 50k）；超限返回 `too-complex`。
- 另提供 `HasLineOfSight(a, b, bodyRadius)`：0.25m 采样 + clearance 校验，供攻击决策与判定帧使用。

**查询预算**：

- 200 只怪、每只 1s 重寻 = 200 q/s；15Hz 下约 **13.3 q/tick**。
- `MaxQueriesPerTick = 16`（240 q/s，留余量）；超出预算的怪物沿用旧路径，下 tick 再算。
- 请求超过 8 tick（约 0.5s）未处理就丢弃；寻路失败后 30 tick（2s）退避。
- 基准测试验证 A* 平均耗时；单次 > 0.5ms 时降低重寻频率或加路径缓存（backlog）。

### 4.4 路点跟随与移动接线

`PathFollower`：

- 保存 `NavPath`、当前路点索引、最终目标、上次重寻 tick。
- 期望方向 = 当前路点 − 当前位置（XZ 归一化）。
- 推进条件：进入半径 **或** 已越过路点平面（`dot(pos - wp, next - wp) > 0`）；半径 `bodyRadius*0.5`。**执行偏差（Task 7）**：原定 `max(bodyRadius*0.5, moveSpeed*0.25)` 在 Wolf（3.5 m/s）上是 0.875m，会在墙角路点前提前跳点、直线切过墙格（绕墙追击验收 t=48 进墙）；已改为 `bodyRadius*0.5`。若将来要恢复 moveSpeed 项，必须证明/实现额外的墙角保护（例如推进前校验下一段 LOS）。
- 重寻：每 15 tick（按 `npcId % 15` 错峰）、目标偏离路径终点 > 2m、当前路点不可达、ORCA 后实际位移偏离路径 > 1.5m。
- 卡死：每 10 tick 检查，连续 3 个窗口（约 2s）位移 < 0.2m 且不在攻击距离内 → 强制重寻；连续 2 次仍卡 → 进入 Evade/Return。

`SimpleMoveSystem`：

- 新增 `MoveTowards(m, direction, dt)`，内部保持现有顺序：`direction * moveSpeed * deltaTime` → ORCA → 积分。
- `MoveTo` 保留为“方向 = 目标 − 当前位置”的薄包装；**无邻居时逐位不变**的护栏继续成立。
- `m.Moving` 按实际位移 > epsilon 判定，避免 ORCA 把位移清零后客户端还在播 Run。
- 慢速状态通过 `moveSpeed` 乘子影响期望位移。

**ORCA 邻居速度修正**：`AvoidanceSystem.EnsureSnapshot` 现在用 `Speed × 朝向` 近似邻居速度；路径跟随在拐角处朝向滞后于位移方向，会喂错速度。改为 NPC/bot 使用**上一 tick 的实际位移**（当前 `Position` − 上次快照 `Position`），玩家保持零速度（不可信输入）。实体死亡/移除/重生时清历史；新 id 首 tick 速度为 0。对称四体收敛场景因此从旧口径下的永久漩涡（最小间距 0.9497、约 5% 交叠）变为稳定最优环形（半径 1/√2 ≈ 0.707m，相邻间距 = 半径和 1.0，零交叠）；既有 `AvoidanceSystemTests` 的 liveness 断言按新语义更新——停住是几何上无法再靠近的正确收敛解。

### 4.5 目标/巡逻/出生点投影

- Chase 目标取玩家位置的 navmesh 投影；投影失败或距离 > `ProjectionRadius` 视为不可达。
- 攻击距离用“怪物 → 投影点”判定，避免玩家站在岩石上时永远差一点。
- 巡逻点不用“投影兜底”：在巡逻圆环内做确定性采样（种子 = npcId），找 0.5m 内、`clearance ≥ bodyRadius` 的可行点；采样 N 次失败再缩半径。生成点存入 `MonsterState`。
- 固定布局出生点在启动时投影校验；超出 `SpawnProjectionTolerance = 0.5m` 直接报错。`SpawnPosition` 使用投影后的点，回巢 `HasReached` 才有效。
- **攻击 LOS**：`Attack` 决策前查 `HasLineOfSight`；`CombatResolver` 判定帧对 NPC caster 再查一次，防止起手后玩家躲到墙后仍被命中。

### 4.6 回巢与位置不连续

- v1 不做 teleport。回巢超时/卡死走 **despawn + respawn**，避免客户端插值把大跳变渲染成横穿地图的滑行。
- Evade 超时使用独立的短重生延迟 `EvadeRespawnDelayTicks = 30`（约 2s），**不是**死亡重生 450 tick（30s）。
- 回巢到家重置 HP 时必须置 `FieldMask.Health` 脏位，否则客户端血条不更新。

### 4.7 地图注册与大地图扩展

`Server/Config/maps.json`：

```json
{ "maps": [ { "id": "demoday", "navFile": "Config/nav/demoday.navbin" } ] }
```

- `NavigationService.LoadMap(mapId, navPath)`；房间/`PlayerState.ZoneId` 选择地图。
- v1 只有一张图，全部瓦片常驻。
- 未来流式加载实现 `EnsureTiles(bounds)`；A* 在未加载瓦片边界返回 `tile-not-loaded`，由上层请求加载。
- 文件格式与接口保留 mapId、瓦片坐标、边界，不提前做分层导航。

**回退**：`NavigationService` 为 null 或地图未加载时，`PathFollower` 退回当前直线移动。既有服务端单测、`--no-avoid` 对照组、未配置导航的宿主都不红。新增 `--no-nav` 负对照。

### 4.8 导航测试

服务端单测（`Server.Tests/Navigation/`）：

- 格式：magic/version/hash、截断文件、错误版本、空瓦片。
- 图：共享边邻接、内部边数量、边界边、clearance、瓦片边界、AABB 索引。
- 寻路：直线、U 形障碍、窄通道（大/小 `bodyRadius` 分流）、不可达孤岛、起终点投影、确定性、漏斗拐点、路径校验、`MaxExpandedNodes`。
- 跟随：路点推进/越过平面、重寻节流、失败退避、卡死判定、ORCA 无邻居逐位不变、实际速度快照。
- 预算：200 个请求分摊后每 tick 不超过 16；请求过期丢弃。
- `NavMeshGraph.FromTriangles(...)` 内存构造，单测不依赖文件；`FromFile` 只做 IO 包装。

Unity EditMode：

- `NavMeshExportFreshnessTests`（canonical hash 一致）。
- DemoDay 覆盖：固定出生/巡逻点在 navmesh 上，已知障碍点不可通行。

E2E：

- `MonsterWorldTests` 增加“绕岩石/湖岸追击”：路径长度 > 直线距离，怪物位置不进入非 navmesh 区域；`--bots 0` 或低数量，避免 bot 人墙误判。
- SpiderBoss 单独断言 clearance ≥ 1.74；抽帧确认不穿模。

## 5. AI 行为层

### 5.1 顶层模式机

`MonsterMode = Patrol | Combat | Evade | Dead`，替换现有 `AlertLevel` + `Return` 分支。

| 从 | 到 | 触发 |
|---|---|---|
| Patrol | Combat | 感知到目标，或受到直接伤害（进入仇恨表） |
| Combat | Evade | 离出生点 > `leashRadius`；或目标超出 `dropAggroRadius` 且没有其他有效仇恨目标；或没有有效目标（死亡/离线）超过 `targetLostGraceTicks` |
| Evade | Patrol | 回到出生点；重置 HP / 仇恨 / 状态；进入 `postEvadeGraceTicks`（30 tick）不感知 |
| 任意 | Dead | HP ≤ 0（沿用现有死亡展示与重生） |
| Dead | 重生 | 现有 `RespawnTicks`，新 id |

- 眩晕/冰冻不是模式，由 `IsControlLocked` 门控：不决策、不移动、不施法，保留当前模式；解除后行为树重新评估。
- 受击硬直保留 `HitDisplayTicks`，只打断前摇，不切模式。
- 前摇锁目标：`AttackResolveTick` 期间不切换目标；威胁照记，判定后再切换。
- 目标丢失 = 死亡/离线/超出 `dropAggroRadius`；不因离开视野锥丢目标（绕墙追击会断）。
- 感知获取只在 Patrol 做；Combat 中新目标只靠伤害/嘲讽，避免路过就拉一群。
- 每 tick 顺序：状态 tick → 死亡检查 → 控制锁 → 仇恨/目标有效性 → 模式转移 → 模式行为 → 位置/动作 flush → 施法 drain → 判定 drain（伤害回调）→ 快照。

### 5.2 仇恨表与目标选择

`ThreatTable`：

- `Dictionary<long, float>`；`Add`、`Remove`、`Clear`、`Highest(valid)`。
- 威胁来源：直接伤害（`damage × threatPerDamage + threatFlat`）、首次感知（`threatInitial`）、嘲讽 API（`Taunt(playerId, durationTicks)`，目标威胁临时置顶）。
- 不衰减（v1 简单）；Evade 时 `Clear()`；目标死亡/离线/离开 `dropAggroRadius` 时 `Remove`。
- 目标选择：最高威胁优先；当前目标保留 20% 滞后（`targetSwitchThreshold`），避免来回抢；同分按 `playerId` 升序，保证确定性。
- 仇恨持久：进战斗后 `LastKnownTargetPosition` 每 tick 从目标实体更新（服务端知道位置），不再要求目标一直在视野锥内。
- 伤害归属：`CastHandler.ApplyHit` 在主循环内调用 `MonsterModule.OnDamageTaken(npcId, attackerId, damage, DamageKind)`；bot（未登录）忽略；格挡命中按实际伤害计的威胁；DoT 加威胁但不触发受击硬直。
- 配置默认：`threatPerDamage=1.0`、`threatFlat=10`、`threatInitial=10`、`targetSwitchThreshold=0.2`、`dropAggroRadius=leashRadius`。

### 5.3 多技能选择

配置从 `SkillIdOf` 硬编码改为模板技能列表：

```json
"skills": [
  { "skillId": 50001, "priority": 0,  "minRange": 0, "maxRange": 1.5, "hpBelow": 1.0 },
  { "skillId": 50005, "priority": 10, "minRange": 2, "maxRange": 6.0, "hpBelow": 0.5 }
]
```

- `attackRange` 保留为旧字段与基础近战默认值；多技能选择以 `skills` 列表为准，避免两处范围配置漂移。
- `minRange`/`maxRange` 可选，默认取 `CombatRules` 表值；`hpBelow`/`hpAbove` 为自身血量比例条件；`priority` 高者优先。
- 冷却只认 `CombatRules`；`SkillSelector` 过滤：技能存在、冷却就绪、目标距离在区间、血量条件满足、可选 LOS；排序 `priority` 降序 → `skillId` 升序。
- 无可用技能返回 null；战斗动作顺序：`Selector(可攻击→Attack, 目标太近且技能有 minRange→Reposition, Chase)`。
- `MonsterCastBuilder` 从选中的 `SkillEntry` 构建 `CastRequest.Shape`：
  - Single：`ShapeType=0`、`Range=min(config maxRange, entry.MaxRange)`、`Angle=entry.MaxAngle`；
  - Circle：`ShapeType=1`、`AreaRadius=entry.MaxAreaRadius`、`Range=0`；
  - 投射物：形状为零；CircleAtPoint 的落点取目标位置并夹到 `MaxImpactDistance`；
  - 入队前用 `CombatRules.ValidateShape` 校验。
- 客户端：
  - `CastBroadcast.SkillId` 已驱动 VFX；新怪物技能要登记进客户端 VFX catalog；
  - 攻击动画变体改为 **skillId → variant** 映射，v1 只映射到现有 0/1 两段；未知技能回退时间戳变体；
  - `NpcAnimationState.Skill` 在客户端 `MapState` 里被忽略，v1 统一 `ActionState=Attack` + VFX，不用 Skill 状态；
  - 新技能 `HitDelaySeconds` 必须按动画判定帧实测填入 `CombatRules`，不能沿用占位 0.35s。
- 启动校验：每个 skillId 在 `CombatRules` 内、范围合法、至少一条可用技能；`MonsterModuleTests.Template_AttackRange_Fits_Its_Combat_Rule` 改为遍历全部技能。

### 5.4 脱战回巢与重置

- 触发：离出生点 > `leashRadius`；或目标超出 `dropAggroRadius` 且没有其他有效仇恨目标（立即）；或没有有效目标（死亡/离线）超过 `targetLostGraceTicks`（45 tick ≈ 3s）。
- Evade 行为：清空仇恨表、`LastKnownTargetPosition=null`、不施法；寻路回 `SpawnPosition`；受击不重新仇恨（避免拉锯），但仍正常扣血/死亡。
- 到家（0.5m）：HP=MaxHp（置 `FieldMask.Health` 脏位）、清状态、回 Patrol、重置巡逻索引、进入 `postEvadeGraceTicks`（30 tick）不感知；伤害/嘲讽仍可重新仇恨。
- 回巢失败/卡死超过 `returnTimeoutTicks`（300 tick ≈ 20s）：despawn + respawn（短延迟 30 tick），不 teleport。
- 受击/眩晕期间不能触发 Evade；眩晕结束后再判断。
- `leashRadius` 默认 `detectionRadius + patrolRadius + 10`，显式可配；测试断言 leash > detection + patrol。

### 5.5 控制 / 击退 / 状态（玩家 → 怪物）

作用方向只做玩家技能 → 怪物；怪物对玩家施加控制留 backlog，不动玩家输入权威。

`MonsterStatusController` 每怪一份：

| 状态 | 效果 | 持续/叠层 |
|---|---|---|
| Stun / Freeze | `IsControlLocked=true`：不决策、不移动、不施法；打断前摇 | 时长刷新，不叠层 |
| Slow | 移动速度 × `(1-magnitude)`，夹到 `[0.1,1]` | 刷新，取最大值 |
| Burn | 每 `tickInterval` 造成一次 DoT；伤害归属施法者并加仇恨 | 刷新，不叠层 |
| Knockback | 每 tick 施加衰减冲量位移；不走 ORCA，落地后投影回 navmesh | 一次性冲量 |

**施加链路**：

- `Server/Config/status_effects.json`：`skillId → [{kind, durationMs, magnitude, tickIntervalMs, chance}]`；只对玩家技能生效。
- `CastHandler.ApplyHit` 在扣血后、目标未死且未被格挡时调用 `MonsterModule.ApplyStatus(targetNpcId, skillId, sourcePlayerId)`；被格挡的命中不施加控制。
- 多段投射物同一施法对同一目标只施加一次状态，沿用 `GroupHitIds` 去重。
- 初始验收映射：20002（Spin）→ Freeze 1.5s；30001（陨石）→ Burn 3s（每 1s 一跳，每跳 5 伤害）。最终由配置表决定。
- 每模板可选 `controlResist`（0–1），Boss 默认 0.5，其余 0；只作用于 Stun/Freeze/Slow/Knockback，Burn 不受影响。

**关键交互**：

- **DoT 不触发受击硬直**：伤害事件必须带 `DamageKind`（Direct/Dot/Environmental）；只有 Direct 置 `HitStartTick`；DoT 只更新 `LastHp`、仇恨、死亡。`LastHp` 每 tick 同步，避免下次直接命中把累计掉血误判成一次大受击。
- **眩晕打断已入队施法**：`MonsterModule` 清 `AttackResolveTick` 不够，`CastHandler` 里的 `PendingCast` 仍会结算。给 `CombatResolver` 注入 `Func<long,bool> isCasterDisabled`（由 MonsterModule 提供），判定时跳过被控施法者；或在控制开始时 `CancelPendingCasts(casterId)`。
- **攻击判定帧再查 LOS**：`CombatResolver` 对 NPC caster 在 `DrainResolved` 时再查一次 `HasLineOfSight`；防止起手后玩家躲墙。
- **击退可能把怪推出 navmesh**：落地后投影回最近三角面；投影失败则保持原位并强制重寻路。控制锁只挡主动移动，击退作为强制位移仍然生效。
- **状态清除**：Evade 到家、死亡、重生时清空；`StatusMask` 置脏，客户端立即更新。

**同步与表现**：

- `EntityFieldMask.Status = 1 << 9`；`EntitySnapshot.StatusMask`（ProtoMember 16，u16，位定义在共享 `Cool.Protocol`）。
- 状态是**状态**不是事件：AOI 新观察者首帧要像 `Dead` 一样无条件带 Status 位；状态变化进 AOI 脏位；`PlayerState.StatusMask` 作为快照来源（玩家恒 0）。
- `DamageResult.IsDot`（ProtoMember 9）：Burn 的合成伤害带该位。
- 客户端：
  - `CombatNetworkBridge` 的 NPC 分支按 `IsDot` 发 `MonsterDotDamageEvent`（只飘字，不触发顿帧/震屏/命中粒子）；否则维持 `MonsterTakeDamageEvent`。
  - `RemoteCastPresenter.OnDamageResult` 对 `IsDot` 直接 return，避免反复引爆投射物 VFX。
  - `NpcEntityAnimator.ApplyStatus(mask)`：Freeze/Stun 进入控制锁时清 `_busy`/`_awaitingConfirm`、`ResetTrigger(Attack)`、清该实体在 `NpcAttackSchedule` 的待播项；冻结期间 `ApplyState` 只记录最新状态不写参数；解锁/死亡时恢复 `Animator.speed`、强制回写最新状态。否则冻结中死亡会卡动画。
  - 状态染色复用 `BodyTintRenderer`（MaterialPropertyBlock），Freeze 蓝白、Burn 橙红；`BodyTintRenderer` 只认 `_Color`，材质没有该属性会静默跳过，先在实际 NPC 预制体上验证。与 `HitFlashVFX` 的 `_Color` 争用用“闪白后重铺状态色”解决；v1 不做 Burn 脉冲，静态染色即可。
  - `RemoteEntityRegistry` 升档时必须 `Apply(StatusMask)` + `Reapply()`；Proxy 档存 mask；视图创建/复用/销毁时清状态与染色。
  - 联网时关闭客户端本地状态效果（`SkillFreezeEffector`/`StatusEffectManager`），服务端 `StatusMask` + Dot 事件是唯一真相，避免双表现。
- `BandwidthBudgetTests` / `ProtocolShapeGuardTests` 更新；全字段快照上限包含状态位。
- Burn 的合成 `DamageResult` 走现有 `CollectDamageRecipients`（受击者 + 观察者），`SourceKind=Player`、`SkillId=来源技能`、`TargetRemainingHp` 正确。

### 5.6 AI 行为测试

服务端单测：

- 仇恨：伤害归属、排名、切换滞后、嘲讽、死亡/离线移除、绕墙持久、前进摇锁定。
- 多技能：距离/血量/冷却/优先级选择、无技能回退、形状构建与校验、确定性、多段状态去重。
- 脱战：三种触发、清仇恨、回血脏位、受击不重新仇恨、超时 despawn/respawn、post-evade grace、优先切换其他仇恨目标。
- 状态：施加/刷新/过期、控制锁、打断前摇与 pending cast、减速、Burn 不触发硬直且照常跳伤害、击退投影、Evade/死亡清状态、controlResist。
- 集成：两名玩家不同威胁 → 目标切换；Boss 多技能；状态位快照。

Unity：

- `NpcAttackSchedule` 的 skillId → variant 契约；
- `NpcEntityAnimator.ApplyStatus` 的染色/恢复/忙位/待播攻击清理；
- `CombatNetworkBridge` 的 `IsDot` → `MonsterDotDamageEvent` 分支；
- `BandwidthBudgetTests` 新状态位；
- PlayMode/E2E：第二玩家抢仇恨、脱战回满、冰冻/灼烧可见、多技能 VFX、冻结中死亡动画正常。

## 6. 客户端单机 AI 删除（P4）

删除不是删文件夹，顺序必须是：

1. **GUID/依赖审计**：预制体引用是 GUID，按名字 grep 不可靠。用 `AssetDatabase.GetDependencies`/GUID 扫描找出 `MonsterEntity`、`MonsterSpawner`、`MonsterMovement`、`AIBrain`、`MonsterConfig` 的引用。
2. **先清场景/预制体组件**：`SlimePBR`/`TurtleShellPBR` 等预制体带 `MonsterEntity`；DemoDay 有两个 `MonsterSpawner`；怪物预制体可能带 `NavMeshAgent`。先移除组件，避免 Missing Script。
3. **再删脚本**：`Monster/AI/*`、`MonsterMovement`、`MonsterSpawner`、`SpawnLedger`、仅单机使用的 `MonsterEntity`/`MonsterStats`/`DamagePipeline`；保留被表现层/编辑器测试仍然使用的配置。
4. **改/删测试**：`NpcViewGameplayStripTests`（前提是预制体带 `MonsterEntity`）、`MonsterConfigNumbersTests`、`MonsterEntityDotDamageTests`、`StatusEffectWiringTests` 需要迁移到服务端配置或删除。
5. **文档**：更新 `Assets/Scripts/Hotfix/GameSystems/Monster/README.md`、根 `CLAUDE.md` 模块索引、P5 指南的 backlog。
6. **保留**：客户端 `NavMeshSurface`/navmesh 资产供导出器使用；`com.unity.ai.navigation` 包保留。

## 7. 测试与验收总表

### 7.1 P1 导航

1. 导出文件可重复生成（同 hash）。
2. 合成 navmesh：A* 绕障碍、漏斗直线、不可达返回失败、窄通道按 bodyRadius 分流。
3. DemoDay：Wolf 绕岩石/湖岸追击路径长度 > 直线距离，采样点全部在 navmesh 上且 clearance ≥ bodyRadius；SpiderBoss 单独断言 ≥ 1.74。
4. 无 nav 数据时旧测试全绿；`--no-nav` 负对照。
5. 200 怪查询预算 ≤ 16/tick；A* 基准耗时记录。

### 7.2 P2 行为

1. 两名玩家：后攻击者超过切换阈值后成为目标；前摇中不切换。
2. 多技能：按距离/血量/冷却/优先级选择；无技能时 Reposition/Chase；形状校验通过。
3. 脱战：超出 leash 回巢、到家满血、仇恨清空、grace 内不感知；超时 despawn/respawn。
4. 目标死亡后优先切换其他仇恨目标，没有才 Evade。

### 7.3 P3 状态

1. Burn 只发 `MonsterDotDamageEvent`，不触发 HitStartTick、不改变行为树、不触发投射物 VFX。
2. 冻结清忙位/待播攻击；冻结中死亡恢复 Animator.speed；解锁回写最新快照。
3. AOI 外冻结 → 进视野首帧生效；Proxy → Full 升档生效。
4. 起手后玩家躲墙后，判定帧不命中。
5. 持续伤害/控制状态在 Evade、死亡、重生后清除。
6. 状态染色与受击闪白叠加不丢色。

### 7.4 P4 清理

1. 联网模式无回归：`MonsterWorldTests`、`CombatSyncTests`、`TwoClientWorldTests` 等通过。
2. DemoDay 与预制体无 Missing Script；无启用中的 `MonsterSpawner`。
3. 文档与模块索引同步。

## 8. 风险与缓解

| 风险 | 缓解 |
|---|---|
| `NavMesh.CalculateTriangulation()` 在 EditMode 取不到 surface 数据 | Task 0 spike 打印三角面数；为 0 时用 `NavMeshSurface.BuildNavMesh()` 或临时场景兜底；导出器断言非空 |
| 重烘后未导出，服务端用旧地形 | canonical hash freshness 测试；场景 hash warning；关卡变更清单 |
| 漏斗/portal 左右顺序错误 | winding 归一化；L 形/U 形合成用例；路径采样校验 |
| clearance 过滤过于保守，切断可行路径 | 0.9/1.1 双阈值；路径校验 + 保守重试；DemoDay 实际跑 + 抽帧；仍不行再烘大半径 navmesh |
| 寻路 + ORCA 互相推挤导致卡死 | 卡死检测、重寻、失败退避、Evade/despawn 兜底 |
| 状态表现与受击闪白/死亡动画冲突 | `ApplyStatus` 控制锁与恢复顺序；MPB 重铺；冻结中死亡恢复 Animator.speed |
| DoT 被当成普通命中 | `DamageKind` + `IsDot` + `MonsterDotDamageEvent`；单测钉住 |
| 客户端删除破坏预制体/场景 | 先 GUID 审计、先清组件再删脚本、测试同步迁移 |
| 多技能内容与客户端资产不同步 | 启动校验 + VFX catalog 登记 + 动画命中帧实测 |
| 大地图扩展返工 | 瓦片格式、mapId、`EnsureTiles` 入口现在就留 |

## 9. 默认决策与 backlog

### 9.1 默认决策

1. Evade 到家**满血重置**，不是持续回血。
2. Evade 期间受击**不重新仇恨**，但仍可被打死。
3. 仇恨**不做战斗中衰减**，脱战即清。
4. 嘲讽只提供 API 与单测，v1 不新增玩家嘲讽技能。
5. 初始状态映射用 20002 冰冻 / 30001 灼烧做验收，最终由配置表决定。
6. Boss `controlResist=0.5`，其余 0；只影响控制，不影响 Burn。
7. 多技能验收给 DogKnight 加一个中距离技能、SpiderBoss 保留近战 + 血量 50% 以下技能，具体数值后续调。
8. 攻击 LOS 只对怪物施法生效；玩家投射物/攻击的静态遮挡不在本期。
9. 回巢超时用短延迟 despawn/respawn，不 teleport。
10. v1 不做路径缓存、不做分层导航、不做多楼层投影。

### 9.2 backlog

- 无缝大世界瓦片流式加载与跨块寻路。
- 动态障碍/NavMeshObstacle/NavMeshLink 支持。
- 玩家投射物/攻击的静态遮挡。
- 怪物对玩家施加控制/状态（需要玩家移动权威化）。
- 仇恨治疗威胁、仇恨衰减曲线、嘲讽技能。
- 状态叠层/免疫/递减/驱散；Boss 阶段与狂暴。
- 路径缓存/LRU、分层导航、多 Agent 类型 navmesh。
- Burn 脉冲视觉、正式状态 VFX、状态图标。
- 寻路调试可视化/`--navprobe`。

## 10. 附录：关键配置与接口

### 10.1 monster_config.json 新增字段

```json
{
  "templateId": 3,
  "name": "DogKnight",
  "hp": 300,
  "moveSpeed": 2.8,
  "bodyRadius": 0.88,
  "detectionRadius": 18,
  "visionAngle": 120,
  "attackRange": 2.2,
  "patrolRadius": 6,
  "leashRadius": 34,
  "controlResist": 0.0,
  "skills": [
    { "skillId": 50003, "priority": 0, "minRange": 0, "maxRange": 2.2 },
    { "skillId": 50005, "priority": 10, "minRange": 2, "maxRange": 6.0, "hpBelow": 0.5 }
  ]
}
```

### 10.2 status_effects.json 示例

```json
{
  "effects": [
    { "skillId": 20002, "kind": "Freeze", "durationMs": 1500, "chance": 1.0 },
    { "skillId": 30001, "kind": "Burn", "durationMs": 3000, "magnitude": 5, "tickIntervalMs": 1000, "chance": 1.0 }
  ]
}
```

### 10.3 关键接口

```csharp
// Server/Navigation/NavigationService.cs
public sealed class NavigationService
{
    public bool LoadMap(string mapId, string navPath);
    public NavPath FindPath(NavQuery query);
    public bool TryProject(Vector3 point, float maxDistance, out Vector3 projected, out float clearance);
    public bool HasLineOfSight(Vector3 from, Vector3 to, float bodyRadius);
}

public readonly struct NavQuery
{
    public readonly string MapId;
    public readonly Vector3 Start;
    public readonly Vector3 Goal;
    public readonly float BodyRadius;
    public readonly int MaxExpandedNodes;
}
```

```csharp
// Server/Monster/Status/MonsterStatusController.cs
public bool IsControlLocked { get; }
public float MoveSpeedMultiplier { get; }
public void Apply(StatusEffectSpec spec, long sourcePlayerId, uint tick, float controlResist);
public void Tick(uint tick, MonsterState state);
public void Clear();
public ushort ToMask();
```

### 10.4 协议新增

- `EntityFieldMask.Status = 1 << 9`。
- `EntitySnapshot.StatusMask`：`[ProtoMember(16)] ushort`；位定义共享。
- `DamageResult.IsDot`：`[ProtoMember(9)] bool`。
- `MonsterStatusMask`：Stun / Freeze / Slow / Burn / Knockback 位；玩家恒 0。

### 10.5 关键文件

- 导航：`Server/Navigation/NavMeshGraph.cs`、`NavMeshPathfinder.cs`、`NavigationService.cs`。
- 导出：`Assets/Scripts/Editor/Navigation/NavMeshExporter.cs`。
- AI：`Server/Monster/Brain/MonsterBrain.cs`、`ThreatTable.cs`、`TargetSelector.cs`、`SkillSelector.cs`、`LeashController.cs`。
- 状态：`Server/Monster/Status/MonsterStatusController.cs`。
- 移动：`Server/Monster/Movement/PathFollower.cs`、`SimpleMoveSystem.cs`、`Server/Movement/AvoidanceSystem.cs`。
- 战斗接线：`Server/Handler/CastHandler.cs`。
- 客户端：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcEntityAnimator.cs`、`NpcAttackSchedule.cs`、`RemoteCastPresenter.cs`、`CombatNetworkBridge.cs`、`RemoteEntityRegistry.cs`。
- 协议：`Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs`、`Combat.cs`。
