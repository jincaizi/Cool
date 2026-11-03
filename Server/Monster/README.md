# Server/Monster —— 服务端权威怪物

`MonsterModule.cs` 在主循环线程独占运行（无锁），约 15Hz（66ms）。每 tick：
状态/死亡 → 控制锁（P3 预留）→ 仇恨有效性 → 模式转移 → 模式行为 → 位置 flush →
施法 drain → 判定 drain → 快照。

## 文件

| 文件 | 作用 |
|---|---|
| `MonsterModule.cs` | 模块入口、tick 编排、死亡/重生、Evade 超时调度 |
| `MonsterState.cs` | 怪物状态：模式/目标/仇恨/前摇/硬直/死亡 |
| `MonsterTickContext.cs` | tick 上下文 |
| `Brain/MonsterBrain.cs` | 顶层模式机：有效性清理、感知、目标选择、Patrol/Combat/Evade 转移、到家重置 |
| `Brain/ThreatTable.cs` | 仇恨表 + `TargetSelector`（20% 切换滞后、同分按 playerId 定序） |
| `Brain/SkillSelector.cs` | 多技能过滤/排序；`NeedsReposition` / `RepositionDistance` |
| `MonsterCastBuilder.cs` | `SkillEntry` → `CastRequest`，入队前 `ValidateShape` |
| `TargetDetector.cs` | 视野锥感知 |
| `SimpleMoveSystem.cs` | 服务端移动 |
| `Movement/PathFollower.cs` | 导航跟随：重寻、路点推进、卡死检测、失败退避 |
| `MonsterBtActions.cs` | 行为树动作：Patrol / Chase / Attack / Reposition / Evade |
| `Bt/` | 行为树节点：Sequence / Selector / Condition / Action |

## 行为规则

- 模式 `Patrol | Combat | Evade | Dead`；HP ≤ 0 时进入 `Dead` 并清空仇恨/目标。
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
  clearance ≥ `bodyRadius`；Chase 停步用 `SkillSelector.IsWithinAnySkillRange`（目标距离）+ LOS
  （隔墙不停、继续绕行）；无最后已知目标时写 Idle/0 速（防假跑广播）；
  Attack 隔墙返回失败落回 Chase；Evade 沿路径回 `SpawnPosition`。
- **目标保持（P2 起由 `ThreatTable` 接管）**：目标只在死亡 / 离线 / 超出 `dropAggroRadius`
  时移除；**偏出视野锥不会丢目标**（绕墙追击不会断）。感知获取只在 `Patrol` 做，战斗中新目标
  只靠伤害归属或嘲讽进入仇恨表。

## P2 行为

### 仇恨与目标切换

- 威胁来源：直接伤害 `damage × 1.0 + 10`；首次感知 `10`；嘲讽 API（v1 仅单测，无玩家技能）。
- 不衰减；Evade 时 `Clear()`；死亡 / 离线 / 超出 `dropAggroRadius` 的条目每 tick 清理。
- 切换滞后：新目标威胁必须超过当前目标 **20%**（`TargetSwitchThreshold = 0.2`）才换；
  同分按较小 `playerId` 升序，与字典遍历顺序无关（跨进程确定性）。
- 前摇（`AttackResolveTick`）期间锁 `TargetId`，威胁照记，判定后再切换；前摇期
  `LastKnownTargetPosition` 仍每 tick 刷新，怪物以 90°/s 朝目标转向。
- 嘲讽 API `MonsterModule.Taunt`：有效期内覆盖仇恨选择；`Evade` / `Dead` 忽略。

### 多技能

- 配置字段 `skills[]`：`skillId`、`priority`（高者优先）、`minRange`、`maxRange`
  （0 = 取 `CombatRules` 表值）、`hpBelow` / `hpAbove`（自身血量比例条件）。
- 选择顺序：距离区间 → 血量条件 → 冷却（只认 `CombatRules`，50005 4s / 50006 5s）→
  `priority` 降序 → `skillId` 升序；无技能可用时，目标太近走 `Reposition`，否则 `Chase`。
- `50005` DogKnight 中距离（2–6m，priority 10，血量 ≤50%）；`50006` SpiderBoss 低血 AoE
  （shapeType 1，半径 4，血量 ≤50%）。客户端按 `skillId` 固定映射到 Attack01/02 变体：
  `50001/50003/50005 → 0`，其余已知技能 `→ 1`，未知技能回退时间戳变体。
- 后端启动校验：每个模板至少一条技能，`skillId` 必须在 `CombatRules` 内且范围合法
  （`MonsterModule.SpawnMonster` fail fast）。

### 脱战与回巢

- 三种触发：离出生点 > `leashRadius`；当前目标超出 `dropAggroRadius` 且无其他有效仇恨
  目标（立即）；没有有效目标（死亡 / 离线）持续 `TargetLostGraceTicks = 45`（3s）。
- 进入 Evade：清空仇恨表 / 目标 / `LastKnownTargetPosition`，不施法，寻路回 `SpawnPosition`；
  期间受击不重新仇恨（避免拉锯），但仍正常扣血/死亡。
- 到家（≤0.5m）：HP = MaxHp、回 `Patrol`、巡逻索引归零、`PostEvadeGraceTicks = 30`（2s）内
  不感知；伤害 / 嘲讽仍可重新仇恨。
- 回巢超时 `ReturnTimeoutTicks = 300`（约 20s）：despawn，`EvadeRespawnDelayTicks = 30`
  后重生（不 teleport，也不是死亡重生的 450 tick）。
- `leashRadius` 默认 `detectionRadius + patrolRadius + 10`；`dropAggroRadius` 默认
  `leashRadius`。两者都可在配置里显式覆盖。

## 配置

`Server/Config/monster_config.json`；`bodyRadius` 是 ORCA 与导航 clearance 的半径真值。
模板字段：`hp`、`moveSpeed`、`detectionRadius`、`visionAngle`、`attackRange`、`patrolRadius`、
`leashRadius`、`dropAggroRadius`、`skills[]`。导航地图表 `Server/Config/maps.json`
（当前仅 `demoday → Config/nav/demoday.navbin`）。

## 测试

`Server/Server.Tests/Monster/`（`MonsterModuleTests`、`MonsterAcceptanceTests`、`MonsterCombatIntegrationTests`、
`MonsterBodyRadiusTests`、`MonsterWindupTests`、`MonsterNavigationTests`、`ThreatTableTests`、
`SkillSelectorTests`、`MonsterCastBuilderTests`、`BrainTests`、`MonsterEvadeTests`、
`MonsterSkillCombatTests`、`P2ConfigTests`、`MonsterP2AcceptanceTests`）；
`Server/Server.Tests/Navigation/`（`NavDataFormatTests`、`NavMeshGraphTests`、`NavMeshPathfinderTests`、
`NavigationServiceTests`、`PathFollowerTests`、`DemoDayNavBinTests`、`NavMeshBenchmarkTests`）；
PlayMode `MonsterWorldTests`、`AoiWorldTests`。

```bash
# 官方 testhost 在本机 Win32Exception(5)；用 in-process runner（见 Server/README.md）
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Monster"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~BrainTests"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterEvade"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterP2Acceptance"
bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~Navigation"
# Unity EditMode（需 Editor；新鲜度测试会临时打开 DemoDay）
bash tools/unity-tests.sh EditMode NpcAttackScheduleTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests
bash tools/unity-tests.sh EditMode DemoDayNavBinCoverageTests
bash tools/unity-tests.sh EditMode NavMeshExportFreshnessTests
bash tools/unity-tests.sh PlayMode MonsterWorldTests
```

## 已知限制

- **P2 不含控制 / 击退 / 状态**（Stun/Freeze/Slow/Burn/Knockback 属 P3）；Evade 到家“清状态”
  在 P2 无状态可清，P3 接入后需补状态清除与快照脏位。
- **新技能无专属动画 / VFX**：50005/50006 只复用现有 Attack01/02 动画，没有专属施法/命中
  VFX（NPC 施法广播目前不驱动 VFX catalog）；`HitDelaySeconds` 仍是 0.35s 占位
  （未按动画判定帧实测，P3 前补齐）。
- **A2 语义已变**：目标偏出视野锥 **不等于** 丢目标；命中与否只由判定帧夹角与判定锥决定
  （`MonsterAcceptanceTests` 用 55°/110° 静态角度钉住）。旧的“离开视野锥即丢目标”语义已删除。
- P1 导航债未在本轮偿还：单张地图（DemoDay）；x≈0 河谷把 navmesh 分成两个连通分量，
  跨岛路径返回 `blocked`；无动态障碍；`HasLineOfSight` 对玩家点不在 navmesh 上时仍会
  永远失败（岩石上挥不到）；0.5ms/query 性能目标未达成（P1 §11 记录）。
- bot 不走 navmesh（`--bots` 仍走 ORCA 愿望位移），只影响怪物。
- 原点观察者 + 6 怪 e2e 未验收：SpiderBoss 巡逻圈最近 14.0m < 检测 25m，
  可能主动感知原点观察者；`MonsterWorldTests` 用 `--monsters 4`（不含 SpiderBoss）。

## 相关

- [Server](../README.md)
- [Movement](../Movement/README.md) —— ORCA 避让
- [客户端 Monster](../../Assets/Scripts/Hotfix/GameSystems/Monster/README.md)
- [SyncLayer](../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md) —— NPC 视图
