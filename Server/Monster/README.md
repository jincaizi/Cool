# Server/Monster —— 服务端权威怪物

`MonsterModule.cs` 在主循环线程独占运行（无锁），约 15Hz（66ms）。每 tick：
受击 / 死亡转移 → 感知视野锥内最近存活玩家 → 行为树 → 位置 flush → 待施法队列
（由 `Server3C` 经 `Handler/CastHandler` 的 `CombatResolver` 裁决）。

## 文件

| 文件 | 作用 |
|---|---|
| `MonsterModule.cs` | 模块入口、tick、死亡与重生 |
| `MonsterState.cs` | 怪物状态与目标 |
| `MonsterTickContext.cs` | tick 上下文 |
| `TargetDetector.cs` | 视野锥感知 |
| `SimpleMoveSystem.cs` | 服务端移动 |
| `Movement/PathFollower.cs` | 导航跟随：重寻、路点推进、卡死检测、失败退避 |
| `MonsterBtActions.cs` | 行为树动作 |
| `Bt/` | 行为树节点：Sequence / Selector / Condition / Action |

## 行为规则

- 行为树覆盖巡逻 / 追击 / 攻击 / 返回。
- 死亡展示 `DeathDisplayTicks = 30`（约 2s）后移除；`RespawnTicks = 450`（约 30s）后以新 id 满血重生。
- NPC id 段从 `NpcIdSeed = 2_000_000` 开始。
- 断线持续运转：状态全在 `EntityStore` 与本模块，零 per-session 依赖。

## 导航（P1）

导航数据 `Server/Config/nav/demoday.navbin`（Unity `Tools/Navigation/Export Server NavMesh`
从 DemoDay 烘培导出，41103 B / 1701 三角面），由 `Server/Config/maps.json` 按 map id 解析。
`--map demoday` 必须配合 `--monsters N` 才启用；只给 `--map` 时打印
`Navigation: --map 需要配合 --monsters（未启动导航）` 并以纯玩家世界运行。
`--no-nav` 强制回退到无导航的直线移动（负对照 / 回退）。

```bash
dotnet build Server/Server3C.csproj
dotnet Server/bin/Debug/net8.0/Server3C.dll 8000 --monsters 4 --map demoday
# 启动日志：Navigation: map=demoday / Monster mode: 4 monsters spawned
```

- `Server/Navigation/`：三角面图 A* + 漏斗，按 `bodyRadius` 校验路径（硬 / 软 clearance）；
  `NavigationService` 每 tick 16 次 + 25ms 双预算（单条查询内部也带 deadline），预算拒绝下
  tick 立刻重试、不计为寻路失败；另提供投影与 LOS。
- `PathFollower`：每 15–29 tick（相位错峰）或目标移动 >2m 重寻；`goalMoved` 有独立 5 tick
  最小间隔；路点推进阈值 `bodyRadius * 0.5`；10 tick 窗口位移 <0.2m 计为卡住，连续 3 个窗口
  触发重寻；真正失败退避 30 tick。**只有无导航时才调 `SimpleMoveSystem.MoveTo` 直线回退；
  有导航但失败/预算拒绝时停住**（C2）。`Tick` 返回实际是否移动，BT 据此写 `Moving`/Run-Idle。
- **判定帧 LOS**：`CombatResolver` 对 NPC caster 在 `DrainResolved` 再查一次
  `NavigationService.HasLineOfSight`；起手后玩家躲墙不命中（玩家施法不变）。
- 接入：出生点投影容差 0.5m，失败即抛（fail fast）；巡逻点 9 方向投影并要求
  clearance ≥ `bodyRadius`；Chase 停步用目标投影点且必须 LOS（隔墙不停、继续绕行）；
  Attack 隔墙返回失败落回 Chase；Return 沿路径回 `SpawnPosition`。
- 目标保持：已有目标只要存活、已登录且在 `detectionRadius + patrolRadius + 10m` 内就保持，
  并刷新 `LastKnownTargetPosition`（绕墙时目标离开视野锥不会立刻丢）；前摇期除外（沿用视野锥语义）。
  目标丢失后才重新选视野锥内最近的存活玩家。

## 配置

`Server/Config/monster_config.json`；`bodyRadius` 是 ORCA 与导航 clearance 的半径真值。
导航地图表 `Server/Config/maps.json`（当前仅 `demoday → Config/nav/demoday.navbin`）。

## 测试

`Server/Server.Tests/Monster/`（`MonsterModuleTests`、`MonsterAcceptanceTests`、`MonsterCombatIntegrationTests`、
`MonsterBodyRadiusTests`、`MonsterWindupTests`、`MonsterNavigationTests`）；
`Server/Server.Tests/Navigation/`（`NavDataFormatTests`、`NavMeshGraphTests`、`NavMeshPathfinderTests`、
`NavigationServiceTests`、`PathFollowerTests`、`DemoDayNavBinTests`、`NavMeshBenchmarkTests`）；
PlayMode `MonsterWorldTests`、`AoiWorldTests`。

```bash
# 官方 testhost 在本机 Win32Exception(5)；用 in-process runner（见 Server/README.md）
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Monster"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Navigation"
bash tools/unity-tests.sh PlayMode MonsterWorldTests
# Unity EditMode（需 Editor；新鲜度测试会临时打开 DemoDay）
bash tools/unity-tests.sh EditMode DemoDayNavBinCoverageTests
bash tools/unity-tests.sh EditMode NavMeshExportFreshnessTests
```

## 已知限制（P1）

- 只有一张地图（DemoDay）；`maps.json` 加条目并导出 navbin 才能跑第二张。
- DemoDay navmesh 被 x≈0 的河谷分成两个大连通分量：跨河寻路返回 `blocked`
  （Unity `CalculatePath` 也是 `PathPartial`），同岛 `[3] -> [5]` 34.977m 可用；跨岛路径不作验收。
- 无动态障碍，navmesh 只在 Unity 导出时烘培；场景改动需重跑导出器。
- bot 不走 navmesh（`--bots` 仍走 ORCA 愿望位移），只影响怪物。
- `HasLineOfSight` 按 navmesh 逐点判 LOS：玩家所在点不在网格上（0.25m 内无三角面，如岩石上）
  或该点 clearance < 怪物 `bodyRadius` 时，怪物会追到玩家位置但永远无法挥砍（casts=0）。
  P1 未处理；P2 需按投影点判 LOS 或约束玩家位置。
- 原点观察者 + 6 怪 e2e 未验收：SpiderBoss 巡逻圈最近 14.0m < 检测 25m，
  可能主动感知原点观察者；`MonsterWorldTests` 用 `--monsters 4`（不含 SpiderBoss）。
- P1 的 Return 仍是直接回巢路径（无 Evade），脱战后沿 navmesh 回 `SpawnPosition`。

## 相关

- [Server](../README.md)
- [Movement](../Movement/README.md) —— ORCA 避让
- [客户端 Monster](../../Assets/Scripts/Hotfix/GameSystems/Monster/README.md)
- [SyncLayer](../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md) —— NPC 视图
