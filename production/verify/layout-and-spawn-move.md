# 布局收紧 + 出生点第三次搬迁（2026-10-17）

任务：DemoDay 的怪物固定布局（`MonsterModule.SpawnDefaultLayout`）收紧收进陆地，出生点同批
从 `(10, 0.21, -40)` 搬到 `(-15, 0.3, -30)`。坐标由任务给定（实测选定）——**未自行改动任何数值**。
不变量（每个槽位到出生点的 XZ 距离 > 该模板「感知半径 + 巡逻半径」⇒ 出生点挂机不会被任何怪发现）不变。

---

## 1. 改了什么

### 1.1 服务端布局 —— `Server/Monster/MonsterModule.cs`（`SpawnDefaultLayout`）

| 槽位 | 模板 | 旧坐标 | 新坐标 | 距出生点 | 阈值（感知+巡逻） | 余量 |
|---|---|---|---|---|---|---|
| [0] | Wolf(2) | (12,0,0) | **(-30,0,0)** | 33.54 | 15+10 = 25 | +8.54 |
| [1] | Slime(1) | (40,0,0) | **(0,0,-20)** | 18.03 | 8+5 = 13 | +5.03 |
| [2] | Slime(1) | (-40,0,0) | **(0,0,-35)** | 15.81 | 8+5 = 13 | **+2.81（最小）** |
| [3] | Wolf(2) | (0,0,40) | **(10,0,-15)** | 29.15 | 15+10 = 25 | +4.15 |
| [4] | DogKnight(3) | (-20,0,20) | **(10,0,15)** | 51.48 | 18+6 = 24 | +27.48 |
| [5] | SpiderBoss(4) | (-40,0,-25) | **(25,0,20)** | 64.03 | 25+12 = 37 | +27.03 |

- 布局数组按给定重写，槽位顺序不变（[0..5] 模板序列仍是 2/1/1/2/3/4——`DefaultLayout_Carries_Six_Templates_In_Order` 绿）。
- 源码注释保留每槽位「距出生点 / 需 > 的阈值」，旧调试注释（对原点观察者的距离/感知）已按新坐标重算；
  其中「[0] 开局即感知原点观察者」这条**不再成立**，注释里就地改写成新事实并指向本报告（见 §5）。

### 1.2 玩家出生点 —— `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`（经 MCP 改，未手改 YAML）

- 对象：`PlayerSpawnPoint`（`PlayerSpawner._spawnPoint` 指向的 Transform，无父节点 ⇒ local == world）。
  **注意**：`_spawnPoint` 字段类型是 `Transform` 引用（不是 Vector3）——直接对着它打 pathPatches 只会改引用；
  实际改的是该 Transform 的 `localPosition`。
- 命令链：
  1. `gameobject-find {"gameObjectRef":{"name":"PlayerSpawner"}}` → 组件 `Hotfix…PlayerSpawner` 找到
  2. `gameobject-component-get`（`paths:["_spawnPoint"]`）→ 引用指向 `PlayerSpawnPoint`（场景里 `m_Name: PlayerSpawnPoint`）
  3. `gameobject-component-modify {"gameObjectRef":{"name":"PlayerSpawnPoint"},"componentRef":{"typeName":"UnityEngine.Transform"},"jsonPatch":{"localPosition":{"x":-15,"y":0.3,"z":-30}}}`
     → `[Success] was 10 → -15 / 0.2115555 → 0.3 / -40 → -30`（首次试 `m_LocalPosition` 被拒：Reflector 对 Transform 只暴露属性，改用 `localPosition`）
  4. `scene-save {"openedSceneName":"DemoDay"}` → 复核：`scene-list-opened` 报 `IsDirty:false`；
     `gameobject-component-get` 读回 `(-15, 0.3, -30)`；磁盘 YAML `git diff` 仅 1 行：
     `m_LocalPosition: {x: 10, y: 0.2115555, z: -40}` → `{x: -15, y: 0.3, z: -30}`
- y 取 0.3 的原因：该点地面实测 **0.256**（探针，见 §2）——0.3 让胶囊落在其上，重力自己落稳。

### 1.3 测试与文档同步（全部为坐标常量/注释，无增删用例）

| 文件 | 改动 |
|---|---|
| `Server/Server.Tests/Monster/MonsterModuleTests.cs` | `DemoSpawnPosition` → `(-15,0,-30)`（XML 注释补第三次搬迁一句：为什么、引本报告）；`Tick_With_No_Players_Patrols_Monsters` 巡逻中心 40→(0,-20)；`Detection_Aggro_And_Attack_Queue_Targets_Closest_Player` / `Target_Lost_Triggers_Return_To_Spawn_And_Peace_Reset` / `Attack_Decision_And_Action_State_Happen_In_The_Same_Tick` 的玩家夹具 `(0,0,0)`→`(-20,0,0)`（Wolf[0] 移到 (-30,0,0) 后面向原点，原点不再是它的感知锥内）；`Target_Lost…` 的"回巢"期望坐标 (12,0,0)→(-30,0,0)；两处 Slime 注释坐标 |
| `Server/Server.Tests/Monster/MonsterCombatIntegrationTests.cs` | 玩家夹具 `(0,0,0)`→`(-20,0,0)`；追击时长注释 45→35 tick |
| `Assets/Scripts/Tests/PlayMode/MonsterWorldTests.cs` | 顶注/观察窗注释里的旧布局表 → 新坐标；**点名 e2e 夹具语义变了**（[0] 不再开局感知，靠 [3] 巡逻进圈；wolfId 取"最先看到的 Wolf"存在假红风险，需起服复核） |
| `Assets/Scripts/Tests/PlayMode/HitJudgmentAlignmentTests.cs` | 注释"开局离出生点 40m" → 15.8m/[2]、18.0m/[1] |
| `docs/superpowers/guides/p5-monster-server.md` | 固定布局表坐标+行为列按新表重填；避让约束注改"出生点 (-15,0,-30)"；加 2026-10-17 第三次搬迁段（含 e2e 语义变化提醒） |
| `docs/superpowers/guides/webgl-demo-runbook.md` | 加 2026-10-17 搬迁段（挂机安全不变/最近怪换成 [2] Slime/80m AOI 波动消失） |
| `docs/superpowers/guides/network-playtest.md` | 两处（远程实体读数、开局无仇恨）各加 2026-10-17 更新行 |
| `docs/superpowers/guides/webgl-demo-boundaries.md` | 面试话术②后加"第三次搬迁更新这句"；"挂机多久会死"口径块加一句（口径不变） |
| `production/deploy/webgl-demo/start-game-server.cmd` | REM 块加 2026-10-17 第三次搬迁 3 行（新点、新最近怪、AOI 全落 80m 内） |
| `CLAUDE.md` | 「出生点」条：坐标 → `(-15, 0.3, -30)`（地面实测 y≈0.256；2026-10-17 第三次搬迁） |

历史文档**不动**（保留当时事实）：`docs/superpowers/specs/2026-10-12-*`、`docs/superpowers/plans/2026-09-12-*`、
`plans/2026-10-12-*`、`plans/2026-10-13-spawn-and-collision-report.md`、`production/verify/npc-anim-and-ground-fix.md`、
`production/verify/ground-probe.json`。

---

## 2. 探针复核（只读）：6 槽位 + 出生点向下 Raycast

命令：`script-execute`（body-only，主线程；从 y=200 向下、maxDistance 500）→ `console-get-logs` 读回：

| 点 | 模板 | 坐标 | 命中面 | 地面高度 y | 距出生点 | 阈值 | 判定 |
|---|---|---|---|---|---|---|---|
| [0] | Wolf(2) | (-30, 0) | **LowpolyTerrain** | +0.624 | 33.54 | >25 | 命中陆地 ✓ |
| [1] | Slime(1) | (0, -20) | **LowpolyTerrain** | 0.000 | 18.03 | >13 | 命中陆地 ✓ |
| [2] | Slime(1) | (0, -35) | **LowpolyTerrain** | −0.113 | 15.81 | >13 | 命中陆地 ✓（高于水面 −0.38） |
| [3] | Wolf(2) | (10, -15) | **LowpolyTerrain** | +0.162 | 29.15 | >25 | 命中陆地 ✓ |
| [4] | DogKnight(3) | (10, 15) | **LowpolyTerrain** | +0.372 | 51.48 | >24 | 命中陆地 ✓ |
| [5] | SpiderBoss(4) | (25, 20) | **LowpolyTerrain** | +0.490 | 64.03 | >37 | 命中陆地 ✓ |
| 出生点 | — | (-15, -30) | **LowpolyTerrain** | **+0.256** | 0 | — | 命中陆地 ✓（y=0.3 落在其上） |

**结论：7/7 全命中 `LowpolyTerrain`**，没有 Tree*/其它；没有湖底、没有出图。距离与阈值的关系与 §1.1 表一致
（这 7 个距离/阈值数字就是脚本里现算的，不是抄的）。

---

## 3. 三套验证命令与输出摘要

### 3.1 服务端单测

```
dotnet test Server/Server.Tests/Server.Tests.csproj
已通过! - 失败: 0，通过: 236，已跳过: 0，总计: 236，持续时间: 300 ms - Server.Tests.dll (net8.0)
```

条数不变（本次只改坐标与注释，未增删用例）。点名复核（`--filter` 逐条打印，全绿）：

- `New_Layout_Slots_Stay_Out_Of_Reach_Of_Demo_Spawn` **6 个 InlineData（slot 0..5）全绿**——用新坐标重算，未改阈值
- `Idle_Player_At_Demo_Spawn_Sees_No_Cast_From_Any_Monster`（2700 tick = 3 分钟挂机零施法）绿
- `Template_AttackRange_Fits_Its_Combat_Rule`（4 个 InlineData）绿——未改
- 其余布局相关：`Tick_With_No_Players_Patrols_Monsters`、`Detection_Aggro_And_Attack_Queue_Targets_Closest_Player`、
  `Target_Lost_Triggers_Return_To_Spawn_And_Peace_Reset`、`Attack_Decision_And_Action_State_Happen_In_The_Same_Tick`、
  `MonsterCombatIntegrationTests.Npc_Casts_Are_Spaced_By_Cooldown_After_Resolver_Drain`、`AvoidanceSystemTests` 全绿

### 3.2 探针（见 §2）—— 7/7 LowpolyTerrain

### 3.3 Unity EditMode

```
unity-mcp-cli run-tool assets-refresh --input '{"options":"ForceSynchronousImport"}'   → Success
unity-mcp-cli run-tool tests-run --input '{"testMode":"EditMode"}' --timeout 900000
Summary: Status=Passed, Total=386, Passed=386, Failed=0, Skipped=0 (00:00:05.8)
```

仍 **386/386**；没有 EditMode/PlayMode 用例断言旧出生点坐标（PlayMode 里两处只是注释，已更新）。

---

## 4. 全仓 grep 到的其它引用点（已核对/处理）

查法：`10f, 0f, -40f` / `10, ?0\.21` / `10,0,[-−]40` / `DemoSpawnPosition` / `_spawnPoint` / `PlayerSpawnPoint` / `湖东岸` / `出生点`。

| 位置 | 处理 |
|---|---|
| `CLAUDE.md`（出生点条） | 改坐标（见 §1.3） |
| `docs/.../guides/p5-monster-server.md`、`webgl-demo-runbook.md`、`network-playtest.md`、`webgl-demo-boundaries.md` | 已更新（见 §1.3） |
| `production/deploy/webgl-demo/start-game-server.cmd` | 已加 REM 行 |
| `Server/Server.Tests/…MonsterModuleTests.cs` / `MonsterCombatIntegrationTests.cs` | 已更新（见 §1.3） |
| `Assets/Scripts/Tests/PlayMode/MonsterWorldTests.cs` / `HitJudgmentAlignmentTests.cs` | 已更新（注释；见 §5） |
| `docs/superpowers/specs/2026-10-12-dogknight…-design.md`、`plans/2026-10-12-…`、`plans/2026-10-13-spawn-and-collision-report.md`、`plans/2026-09-12-p5-monster-server.md`、`production/verify/ground-probe.json`、`production/verify/npc-anim-and-ground-fix.md` | **历史文档/探针产物，保留原样**（记录当时事实） |
| `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` / `production/verify/DemoDay.unity`(未入库副本) | 前者已改；后者是历史副本，不动 |
| 其它命中（`ClientNetworkManager.SpawnPosition`、`Login.cs`、`Sys3CEntry`、`HitResolver`、`CombatSyncTests` 等） | 是"出生点"的通用词/登录协议字段，与服务端布局无关，未动 |

---

## 5. 疑虑 / 需要人工复核的点（均未自行"修"）

1. **e2e 观察者（原点）夹具语义变了**——旧布局里 [0] Wolf 在 (12,0,0)「开局即感知原点观察者」是
   `--monsters 4` 那条 PlayMode e2e（`MonsterWorldTests`）的支点；新布局 [0] 距原点 30m
   （巡逻圈最近 20m > 检测 15m）**不再感知**，原点观察者挨打只能来自 [3] Wolf（距原点 18.0m，
   巡逻圈有点到原点 11.2m——巡逻中可能进圈）。该用例又取"**最先看到的** Wolf 视图"当 `wolfId`、
   断言伤害来源 == 该 id ⇒ 两只 Wolf 都在 AOI 内时**存在假红风险**（不是必然失败）。
   **本次未跑 PlayMode**：本机已有一个 `Server3C.exe`（PID 48320）在跑**旧二进制**并监听 UDP 8000，
   我未去杀用户进程。请用新构建重启服务端后再跑一次：
   `unity-mcp-cli run-tool tests-run --input '{"testMode":"PlayMode","testClass":"MonsterWorldTests"}' --timeout 300000`
   （`HitJudgmentAlignmentTests` 也依赖起服，本次同样未跑，其注释已更新。）
   另：新布局下 [4] DogKnight（18.0m，检测 18m）与 [5] SpiderBoss（32.0m，检测 25m）的巡逻圈也会
   压到各自检测半径内——**只影响 `--monsters 6` 的 e2e/demo 口径**，与出生点挂机安全不变量无关。
2. **正在跑的演示服务端是旧布局**：要看到新布局必须重启 `dist/server/Server3C.exe`（见上）。
3. **落地高度差（既有待议项，非本次引入）**：怪物根节点按 y=0 落位，探针地面高度
   [0]=+0.624 / [1]=0.000 / [2]=−0.113 / [3]=+0.162 / [4]=+0.372 / [5]=+0.490（旧槽位是 0.07–0.28）——
   [0] Wolf 处地面比根节点高 0.62m，实机上"脚埋进地面"可能更明显（`production/verify/npc-anim-and-ground-fix.md` 已记该现象，
   当时也列为"布局/出生点待议项"）。本次按任务要求**不动**，仅提醒重启服务端后看一眼实机表现。
4. **AOI 读数**：六只怪全部落在出生点的 80m 视距内（最远 [5] 64.0m）⇒ 演示浮层"远程实体"不再有
   80m 边缘进出波动（旧口径 3↔4 / 5↔6 波动消失）。文档已按此更新，但**未做实机复测**（需起服）。

---

## 6. 提交

见本次提交（`fix(demo): …`）——布局收紧 + 出生点第三次搬迁；不变量（全六槽位距出生点 > 感知+巡逻）
用新坐标重算后仍成立，服务端 236/236、EditMode 386/386、探针 7/7 命中陆地。
