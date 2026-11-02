# Server/Navigation —— 服务端导航

服务端怪物寻路：Unity 烘焙的 DemoDay navmesh 导出为 navbin，由 `NavigationService` 加载；
三角面图 A* + 漏斗产出路径，`Server/Monster/Movement/PathFollower` 消费。
每 tick 查询预算 16 次（`NavigationService.MaxQueriesPerTick`），失败不排队。

## 文件

| 文件 | 作用 |
|---|---|
| `NavData.cs` | 内存模型：MapId、AABB、AreaCosts、顶点与分块三角面 |
| `NavDataFormat.cs` | navbin 二进制读写与 canonical / source hash |
| `NavMeshGraph.cs` | 三角面图：邻接、网格索引、clearance、投影、LOS |
| `NavMeshPathfinder.cs` | 三角面 A* + 漏斗 + 按 `bodyRadius` 的路径校验 |
| `NavPath.cs` | 路径结果：`Success` / `FailReason` / `Length` / `Waypoints` |
| `NavigationService.cs` | 地图加载、tick 预算、投影与 LOS 入口 |

## 数据与加载

- Unity 菜单 `Tools/Navigation/Export Server NavMesh` 把 DemoDay 的烘焙 navmesh 导出为
  `Server/Config/nav/demoday.navbin`（41103 B / 1701 三角面）。
- `Server/Config/maps.json` 把 map id 映射到 navFile；`NavigationService.LoadFromConfig`
  按 `AppContext.BaseDirectory` 解析（`dotnet run` / `dotnet test` 的 CWD 不可靠）。
- 启动参数：`--map demoday` 必须配合 `--monsters N`；`--no-nav` 强制无导航（直线移动回退）。
  成功日志 `Navigation: map=demoday`。

## 寻路

- 三角面 A* + 漏斗（funnel），起点 / 终点各允许 2m 投影；按 `bodyRadius` 做 clearance
  硬 / 软校验（0.9 / 1.1 系数）与惩罚；默认最多展开 50000 节点。
- `TryProject` 投影到最近三角面；`HasLineOfSight` 沿线段每 0.25m 采样，要求点在图内且
  clearance ≥ `bodyRadius`。
- 预算拒绝返回 `FailReason="budget"`，调用方下 tick 立刻重试；真正的失败由
  `PathFollower` 退避 30 tick。

## 测试

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~Navigation"
bash tools/unity-tests.sh EditMode DemoDayNavBinCoverageTests
bash tools/unity-tests.sh EditMode NavMeshExportFreshnessTests
```

`Server/Server.Tests/Navigation/`：`NavDataFormatTests`、`NavMeshGraphTests`、
`NavMeshPathfinderTests`、`NavigationServiceTests`、`PathFollowerTests`、`DemoDayNavBinTests`。

## 已知限制

- 只有 DemoDay 一张地图；`maps.json` 加条目并导出 navbin 才能跑第二张。
- DemoDay navmesh 被 x≈0 河谷分成两个大连通分量：跨岛寻路 `blocked`（Unity `CalculatePath`
  同样 `PathPartial`）；同岛 `[3] -> [5]` 34.977m 可通。跨岛路径不作验收。
- 无动态障碍；场景几何改动后需重跑导出器（`NavMeshExportFreshnessTests` 会红）。
- bot 不走 navmesh（仍走 ORCA 愿望位移）。
- 玩家所在点不在网格上或 clearance < 怪物 `bodyRadius` 时 LOS 恒 false，怪物无法攻击
  （P1 已知限制；见 [服务端怪物](../Monster/README.md)）。

## 相关

- [Server](../README.md)
- [服务端怪物](../Monster/README.md) —— `PathFollower` 接入与行为树
- [P5 怪物服务端化指南](../../docs/superpowers/guides/p5-monster-server.md)
