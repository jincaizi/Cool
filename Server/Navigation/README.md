# Server/Navigation —— 服务端导航

服务端怪物寻路：Unity 烘焙的 DemoDay navmesh 导出为 navbin，由 `NavigationService` 加载；
三角面图 A* + 漏斗产出路径，`Server/Monster/Movement/PathFollower` 消费。
每 tick 有**次数 + 时间**双预算（`MaxQueriesPerTick=16`、`MaxMillisecondsPerTick=25ms`），
单条查询内部还有同一截止时间；超限返回 `FailReason="budget"`，下 tick 重试。

## 文件

| 文件 | 作用 |
|---|---|
| `NavData.cs` | 内存模型：MapId、AABB、AreaCosts、顶点与分块三角面 |
| `NavDataFormat.cs` | navbin 二进制读写与 canonical / source hash；写入前校验 + temp+move |
| `NavMeshGraph.cs` | 三角面图：邻接、连通分量、网格索引、clearance、投影、LOS |
| `NavMeshPathfinder.cs` | 三角面 A* + 漏斗 + 按 `bodyRadius` 的路径校验 |
| `NavPath.cs` | 路径结果：`Success` / `FailReason` / `Length` / `Waypoints` |
| `NavigationService.cs` | 地图加载、tick 预算、投影与 LOS 入口 |

## 数据与加载

- Unity 菜单 `Tools/Navigation/Export Server NavMesh` 把 DemoDay 的烘焙 navmesh 导出为
  `Server/Config/nav/demoday.navbin`（41103 B / 1701 三角面）。
- `Server/Config/maps.json` 把 map id 映射到 navFile；`NavigationService.LoadFromConfig`
  按 `AppContext.BaseDirectory` 解析（`dotnet run` / `dotnet test` 的 CWD 不可靠）。
  `{"maps": null}` 会合并成空表（确定性 "map not found"，不是 NRE）。
- 启动参数：`--map demoday` 必须配合 `--monsters N`；`--no-nav` 强制无导航（直线移动回退）。
  `--map` 后接开关或为空会直接报错并退出（exit 2），不会把 `--no-nav` 当 map id。
  成功日志 `Navigation: map=demoday`。

## 寻路

- 三角面 A* + 漏斗（funnel），起点 / 终点各允许 2m 投影；按 `bodyRadius` 做 clearance
  硬 / 软校验（0.9 / 1.1 系数）与惩罚；默认最多展开 50000 节点。
- **连通分量前置剪枝**：图加载时按共享边求连通分量；跨岛查询在 A* 前直接 `blocked`。
- **portal clearance 缓存**：门内最长可通行 clearance 与最佳点按 `(门端点, 量化 maxClearance)`
  缓存（上限 65536 项），跨查询复用；这是 200 怪反复寻路的主要加速项。
- 路径校验每 **0.25m** 采样（原 0.1m）；仍小于最小 `bodyRadius`（0.5m）的一半，
  窄通道/墙角用例全部保留。取舍：极薄（<0.25m）的几何缝隙可能被跳过——DemoDay 不适用。
- `IsPointNavigable` 的 clearance 封顶 2.5m（覆盖 Boss 1.74×1.1）；三角面级 clearance 仍按 8m 计算。
- `TryProject` 返回**点级** clearance（到最近边界边的精确距离），巡逻点判断可站立空间用它。
- `HasLineOfSight` 沿线段每 0.25m 采样，要求点在图内且 clearance ≥ `bodyRadius`。
- 预算拒绝返回 `FailReason="budget"`，调用方下 tick 立刻重试；真正的失败由
  `PathFollower` 退避 30 tick。单条查询被预算中止时，已完成的门缓存仍保留，重试会更快。

## C2：寻路失败时停住，不直线穿地形

`PathFollower` 只在 `_navigation == null` 时回退直线 `MoveTo`。有导航但：

- 查询失败（`blocked` / `off-navmesh` / `too-complex`）、退避中、预算拒绝且**没有旧路径**；
- 路径已走完（终点是投影点，真实目标可能在网格外 ≤2m）；
- 偏离路径 >1.5m 需要重寻

时，**原地停住**（零方向 `MoveTowards` 清零 `CurrentVelocity`），由行为树转入 Idle。
绝不朝 blocked/off-navmesh 目标直线移动。巡逻点全部采样失败时回退到**已投影的巡逻中心**
（单点），不再生成 legacy 未投影圆环。

## I5：Moving / 快照速度按实际位移

`SimpleMoveSystem.MoveTo/MoveTowards` 返回 ORCA 后的实际位移长度；`PathFollower.Tick`
返回本 tick 是否实际移动；`MonsterBtActions` 用它写 `m.Moving` 与 `ActionState`（Run/Idle）；
`MonsterModule` flush 时 `Moving=false` ⇒ 快照 `Speed=0`。

## I1：判定帧 LOS

`CombatResolver.NpcLineOfSight` 由 `Server3C.EnableMonsters` 接线到
`NavigationService.HasLineOfSight`（半径取施法怪物模板）；`DrainResolved` 对 NPC caster
逐目标复查 LOS，起手后躲到墙后的目标不结算。玩家施法不经过这个回调。

## 性能与预算（2026-11-03 最终修复轮实测）

`Server.Tests/Navigation/NavMeshBenchmarkTests`（Release，200 次查询，5/10/20/35m 四档）：

| 档位 | 平均 | 最大 |
|---|---|---|
| 5m | 0.49–0.64ms | 3.8–6.1ms |
| 10m | 0.80–1.08ms | 5.6–9.6ms |
| 20m | 2.09–2.58ms | 13.7–28.8ms |
| 35m | 3.38–4.83ms | 19–74ms（冷缓存 blocked/Boss 1.74） |

- 200 次查询总体平均 1.8–2.3ms、p95 9.8–11.9ms；最大值为 blocked 长查询（冷缓存）。
- 200 怪预算 soak：25ms/tick 下 20 tick 实测 `maxTick≈17–27ms`（Debug/Release 均 < 27ms），
  每 tick 放行 12–16 次查询，其余 `budget` 下 tick 重试。
- **200 怪刷新率**：按 12–16 q/tick × 15Hz ≈ 180–240 q/s，实测 **约 0.9–1.2Hz**，保守验收口径按
  **低于 1Hz** 记录；大半径/Boss 与跨岛 blocked 会进一步拉低（次数预算硬限 16/tick；时间预算在慢查询时额外拒绝）。
- **0.5ms/query 未达成**（5m 档 0.49–0.64ms，最好一次擦线），这是 **P2 性能债**。时间预算保证
  tick 不超时，但路径缓存/分层导航/预计算 clearance 场等优化留到 P2。

## 测试

```bash
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Navigation"
bash tools/test-server-inprocess.sh                      # 全量（本机 testhost workaround）
bash tools/unity-tests.sh EditMode DemoDayNavBinCoverageTests
bash tools/unity-tests.sh EditMode NavMeshExportFreshnessTests
```

`Server/Server.Tests/Navigation/`：`NavDataFormatTests`、`NavMeshGraphTests`、
`NavMeshPathfinderTests`、`NavigationServiceTests`、`PathFollowerTests`、`DemoDayNavBinTests`、
`NavMeshBenchmarkTests`。

## 已知限制 / 验证债

- 只有 DemoDay 一张地图；`maps.json` 加条目并导出 navbin 才能跑第二张。
- DemoDay navmesh 被 x≈0 河谷分成两个大连通分量：跨岛寻路 `blocked`（Unity `CalculatePath`
  同样 `PathPartial`）；同岛 `[3] -> [5]` 34.977m 可通。跨岛路径不作验收。
- 无动态障碍；场景几何改动后需重跑导出器（`NavMeshExportFreshnessTests` 会红）。
- bot 不走 navmesh（仍走 ORCA 愿望位移）。
- 玩家所在点不在网格上或 clearance < 怪物 `bodyRadius` 时 LOS 恒 false，怪物无法攻击
  （P1 已知限制；见 [服务端怪物](../Monster/README.md)）。
- **视觉抽帧未做**：Wolf 绕岩石/湖岸、SpiderBoss 不穿模仍只有服务端几何断言
  （`DemoDayNavBinTests` 的逐点 `IsPointNavigable`），需在 DemoDay Play Mode 人工抽帧确认。
- **导出器三角面排序 tie-break**（`NavMeshExporter.cs` min/max/area 相同但中间顶点不同）未改：
  改导出器必须重导 navbin 并同步 hash/geometry 断言，本轮不重导，记 **P2**。
- `MonsterBtActions` 的 Chase/Attack 各查一次 LOS（同 tick 冗余）；`AvoidanceSystem._secondsPerTick`
  自 2026-11-02 改用实际位移后不再参与计算（保留参数以兼容构造签名）；`NavMeshGraph.Triangle`
  的 AABB 字段只在构造期使用；`PathFollower.WaypointIndex` 目前只有测试观察用途。以上均记录不清理。
- `PathFollower` 周期重寻每轮叠加 `15 + npcId%15` tick（实际周期 15–29 tick），是刻意的降载；
  `goalMoved` 有独立的 5 tick 最小间隔，不允许每 tick 绕过周期闸门。
- 单次查询内部 deadline 检查粒度为一次 A* 扩展 / 16 个校验采样 / 一次 portal clearance；
  实测 tick 超调 <2ms。理论最坏仍受单次 portal 计算时长约束。

## 相关

- [Server](../README.md)
- [服务端怪物](../Monster/README.md) —— `PathFollower` 接入与行为树
- [P5 怪物服务端化指南](../../docs/superpowers/guides/p5-monster-server.md)
